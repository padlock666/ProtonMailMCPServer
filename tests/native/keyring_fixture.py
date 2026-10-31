#!/usr/bin/python3
"""Synthetic service only. Always creates its own non-activating bus and storage.
No inherited desktop address reaches dbus, GNOME or application children.
"""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time

SERVICE = 'org.freedesktop.secrets'
ROOT = '/org/freedesktop/secrets'
COLLECTION = ROOT + '/collection/synthetic'
ITEM = COLLECTION + '/item1'
ATTRS = {'application': 'protonmail-mcp', 'purpose': 'bridge-imap-password',
         'id': '44ec5121-59ce-4c39-8b45-551823feb631'}
CANARY = b'SYNTHETIC-password-sentinel'


def output(value):
    print(json.dumps(value), flush=True)


def main():
    backend = sys.argv[1]
    if backend not in ('fake', 'gnome'):
        raise RuntimeError()
    if backend == 'gnome' and os.environ.get('PROTONMCP_KEYRING_INTEGRATION') != 'I_AUTHORIZE_ISOLATED_SYNTHETIC_KEYRING_TESTS':
        raise RuntimeError()
    children = []
    with tempfile.TemporaryDirectory(prefix='protonmcp-keyring-fixture-') as temporary:
        root = Path(temporary)
        env = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8'}
        for key in ('HOME', 'XDG_CONFIG_HOME', 'XDG_DATA_HOME', 'XDG_CACHE_HOME', 'XDG_RUNTIME_DIR'):
            directory = root / key.lower()
            directory.mkdir(mode=0o700)
            env[key] = str(directory)
        config = root / 'bus.conf'
        config.write_text("""<!DOCTYPE busconfig PUBLIC '-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN'
'http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd'>
<busconfig><type>session</type><auth>EXTERNAL</auth><listen>unix:path=""" + str(root / 'bus') + """</listen>
<policy context='default'><allow send_destination='*'/><allow receive_sender='*'/><allow own='*'/><allow eavesdrop='true'/></policy></busconfig>""")
        env['DBUS_SESSION_BUS_ADDRESS'] = 'unix:path=' + str(root / 'bus')
        # Imports occur only after the fresh environment is installed.
        os.environ.clear()
        os.environ.update(env)
        try:
            bus_process = subprocess.Popen(['/usr/bin/dbus-daemon', '--nofork', '--config-file=' + str(config)],
                                           env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
            children.append(bus_process)
            end = time.monotonic() + 5
            while not (root / 'bus').is_socket():
                if time.monotonic() > end or bus_process.poll() is not None:
                    raise RuntimeError()
                time.sleep(.01)
            import dbus
            import dbus.service
            from dbus.mainloop.glib import DBusGMainLoop
            from gi.repository import GLib
            DBusGMainLoop(set_as_default=True)
            bus = dbus.bus.BusConnection(env['DBUS_SESSION_BUS_ADDRESS'])
            loop = GLib.MainLoop()
            state = {'mode': 'success', 'calls': [], 'loads': 0, 'sessions': [], 'value': CANARY,
                     'content_type': 'text/plain; charset=utf-8', 'algorithm': None, 'clients': set()}
            first = bus
            second = dbus.bus.BusConnection(env['DBUS_SESSION_BUS_ADDRESS'])

            def transition(stage):
                mode = state['mode']
                if mode.startswith('owner-' + stage + '-'):
                    kind = mode[len('owner-' + stage + '-'):]
                    first.release_name(SERVICE)
                    if kind in ('replace', 'back'):
                        second.request_name(SERVICE, 4)
                    if kind == 'back':
                        second.release_name(SERVICE)
                        first.request_name(SERVICE, 4)
                    if kind == 'reacquire':
                        first.request_name(SERVICE, 4)

            class Fake(dbus.service.Object):
                def __init__(self, connection, path):
                    self.fixture_path = path
                    super().__init__(connection, path)

                @dbus.service.method('org.freedesktop.DBus.Properties', in_signature='s', out_signature='a{sv}')
                def GetAll(self, interface):
                    state['calls'].append('GetAll:' + interface.rsplit('.', 1)[-1])
                    if state['mode'] == 'denied':
                        raise dbus.exceptions.DBusException('SYNTHETIC-native-error', name='org.freedesktop.DBus.Error.AccessDenied')
                    if interface.endswith('.Service'):
                        return {'Collections': dbus.Array([], signature='o')}
                    if interface.endswith('.Collection'):
                        return {'Locked': dbus.Boolean(state['mode'] == 'locked-collection'), 'Label': 'synthetic',
                                'Created': dbus.UInt64(1), 'Modified': dbus.UInt64(1), 'Items': dbus.Array([], signature='o')}
                    if interface.endswith('.Item'):
                        attrs = dict(ATTRS, extra='synthetic')
                        if state['mode'] == 'attributes':
                            attrs['purpose'] = 'wrong'
                        return {'Locked': dbus.Boolean(state['mode'] == 'locked-item' or
                                                     (state['mode'] == 'duplicate' and self.fixture_path.endswith('item2'))),
                                'Attributes': dbus.Dictionary(attrs, signature='ss'), 'Label': 'ignored',
                                'Created': dbus.UInt64(1), 'Modified': dbus.UInt64(1)}
                    raise RuntimeError()

                @dbus.service.method('org.freedesktop.Secret.Service', in_signature='s', out_signature='o', async_callbacks=('ok', 'error'))
                def ReadAlias(self, alias, ok, error):
                    if alias != 'default':
                        raise RuntimeError()
                    state['calls'].append('ReadAlias')
                    transition('alias')
                    if state['mode'] == 'hang-load':
                        GLib.timeout_add(10000, lambda: (ok(dbus.ObjectPath(COLLECTION)), False)[1])
                    elif state['mode'] == 'stall-alias':
                        GLib.timeout_add(1200, lambda: (ok(dbus.ObjectPath(COLLECTION)), False)[1])
                    else:
                        ok(dbus.ObjectPath('/' if state['mode'] == 'missing-alias' else COLLECTION))

                @dbus.service.method('org.freedesktop.Secret.Collection', in_signature='a{ss}', out_signature='ao', async_callbacks=('ok', 'error'))
                def SearchItems(self, attributes, ok, error):
                    if dict(attributes) != ATTRS:
                        raise RuntimeError()
                    state['calls'].append('SearchItems')
                    transition('search')
                    paths = [] if state['mode'] == 'missing-item' else [ITEM]
                    if state['mode'] == 'duplicate':
                        paths.append(COLLECTION + '/item2')
                    if state['mode'] == 'stall-search':
                        GLib.timeout_add(1200, lambda: (ok(dbus.Array(paths, signature='o')), False)[1])
                    else:
                        ok(dbus.Array(paths, signature='o'))

                @dbus.service.method('org.freedesktop.Secret.Service', in_signature='sv', out_signature='vo')
                def OpenSession(self, algorithm, parameters):
                    state['calls'].append('OpenSession:' + str(algorithm))
                    # Let real libsecret negotiate its supported plain fallback. No crypto here.
                    if algorithm != 'plain':
                        raise dbus.exceptions.DBusException('unsupported', name='org.freedesktop.DBus.Error.NotSupported')
                    state['algorithm'] = str(algorithm)
                    path = ROOT + '/session/synthetic'
                    return dbus.String('', variant_level=1), dbus.ObjectPath(path)

                @dbus.service.method('org.freedesktop.Secret.Item', in_signature='o', out_signature='(oayays)', async_callbacks=('ok', 'error'), message_keyword='message')
                def GetSecret(self, session, ok, error, message=None):
                    state['calls'].append('GetSecret')
                    state['loads'] += 1
                    transition('load')
                    if state['mode'] == 'malformed-reply':
                        reply = dbus.lowlevel.MethodReturnMessage(message)
                        reply.append('synthetic-invalid-signature', signature='s')
                        self.connection.send_message(reply)
                        return
                    if state['mode'] == 'deleted':
                        error(dbus.exceptions.DBusException('deleted', name='org.freedesktop.Secret.Error.NoSuchObject'))
                        return
                    if state['mode'] == 'lock-during-load':
                        error(dbus.exceptions.DBusException('locked', name='org.freedesktop.Secret.Error.IsLocked'))
                        return
                    def respond():
                        secret = dbus.Struct((session, dbus.ByteArray(b''), dbus.ByteArray(state['value']), state['content_type']), signature='oayays')
                        ok(secret)
                        # Signals queued after the successful reply but before final bus barrier.
                        transition('completion')
                        return False
                    if state['mode'] == 'hang-load':
                        GLib.timeout_add(35000, respond)
                    elif state['mode'] == 'stall-load' or state['mode'].startswith('owner-load-'):
                        GLib.timeout_add(1200, respond)
                    else:
                        respond()

            # Observe only method names/session algorithm on this owned bus, never payloads.
            observer = dbus.bus.BusConnection(env['DBUS_SESSION_BUS_ADDRESS'])
            observer.add_match_string("type='method_call',eavesdrop='true'")
            fixture_senders = {first.get_unique_name(), second.get_unique_name(), observer.get_unique_name()}
            def observe(connection, message):
                if message.get_sender() in fixture_senders:
                    return
                interface = message.get_interface() or ''
                member = message.get_member() or ''
                if interface.startswith('org.freedesktop.Secret'):
                    state['clients'].add(str(message.get_sender()))
                if backend == 'gnome' and interface.startswith('org.freedesktop.Secret'):
                    state['calls'].append(member)
                    if member == 'OpenSession':
                        state['algorithm'] = str(message.get_args_list()[0])
                    if member == 'GetSecret':
                        state['loads'] += 1
                if member in ('Unlock', 'Prompt', 'CreateItem', 'Delete', 'SetAlias', 'Set', 'SetSecret'):
                    state['calls'].append('FORBIDDEN:' + member)
            observer.add_message_filter(observe)
            daemon = None
            objects = []
            if backend == 'fake':
                first.request_name(SERVICE, 4)
                for conn in (first, second):
                    for path in (ROOT, COLLECTION, ITEM, COLLECTION + '/item2'):
                        objects.append(Fake(conn, path))
            else:
                control = root / 'control'
                control.mkdir(mode=0o700)
                def start_daemon():
                    child = subprocess.Popen(['/usr/bin/gnome-keyring-daemon', '--foreground', '--unlock', '--components=secrets',
                                               '--control-directory=' + str(control)], env=env, stdin=subprocess.PIPE,
                                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
                    children.append(child)
                    child.stdin.write(b'SYNTHETIC-collection-password\n')
                    child.stdin.close()
                    end = time.monotonic() + 5
                    while not bus.name_has_owner(SERVICE):
                        if time.monotonic() > end or child.poll() is not None:
                            raise RuntimeError()
                        time.sleep(.01)
                    return child
                daemon = start_daemon()
                gservice = dbus.Interface(bus.get_object(SERVICE, ROOT), 'org.freedesktop.Secret.Service')
                gcollection = str(gservice.ReadAlias('default'))
                if gcollection == '/':
                    raise RuntimeError()
                _, gsession = gservice.OpenSession('plain', dbus.String('', variant_level=1))
                collection_api = dbus.Interface(bus.get_object(SERVICE, gcollection), 'org.freedesktop.Secret.Collection')
                gitems = []

                def create_item(value):
                    item, prompt = collection_api.CreateItem(
                        {'org.freedesktop.Secret.Item.Label': 'synthetic',
                         'org.freedesktop.Secret.Item.Attributes': dbus.Dictionary(ATTRS, signature='ss')},
                        dbus.Struct((gsession, dbus.ByteArray(b''), dbus.ByteArray(value), 'text/plain; charset=utf-8'), signature='oayays'), False)
                    if prompt != '/':
                        raise RuntimeError()
                    gitems.append(str(item))
                # Exercise the documented GI provisioning recipe only on this owned bus.
                text = (Path(__file__).parent / 'KEYRING.md').read_text()
                code = text.split('<!-- provisioning-example:start -->', 1)[1].split('<!-- provisioning-example:end -->', 1)[0]
                code = code.split('```python', 1)[1].split('```', 1)[0]
                from gi.repository import Gio
                recipe_bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
                fixture_senders.add(recipe_bus.get_unique_name())
                namespace = {'__name__': 'synthetic_recipe'}
                exec(compile(code, 'documented_provisioning_example', 'exec'), namespace)
                provisioned = namespace['provision'](ATTRS['id'], CANARY.decode())
                gitems.append(provisioned.get_object_path())
                del provisioned, namespace

            def command(fd, condition):
                nonlocal daemon
                line = sys.stdin.readline()
                if not line:
                    loop.quit()
                    return False
                data = json.loads(line)
                action = data.get('mode', 'success')
                if action == 'stop':
                    loop.quit()
                    return False
                if action == 'stats':
                    output({'calls': state['calls'], 'loads': state['loads'], 'algorithm': state['algorithm'],
                            'active_clients': sum(bool(bus.name_has_owner(client)) for client in state['clients'])})
                    return True
                if backend == 'gnome':
                    if action == 'duplicate':
                        create_item(CANARY)
                    elif action == 'missing-item':
                        for item in gitems:
                            prompt = dbus.Interface(bus.get_object(SERVICE, item), 'org.freedesktop.Secret.Item').Delete()
                            if prompt != '/':
                                raise RuntimeError()
                        gitems.clear()
                    elif action == 'success' and not gitems:
                        create_item(CANARY)
                    elif action == 'rotate':
                        dbus.Interface(bus.get_object(SERVICE, gitems[0]), 'org.freedesktop.Secret.Item').SetSecret(
                            dbus.Struct((gsession, dbus.ByteArray(b''), dbus.ByteArray(b'SYNTHETIC-rotated'), 'text/plain'), signature='oayays'))
                    elif action == 'locked-collection':
                        _, prompt = gservice.Lock(dbus.Array([gcollection], signature='o'))
                        if prompt != '/':
                            raise RuntimeError()
                    elif action == 'pause':
                        os.kill(daemon.pid, signal.SIGSTOP)
                    elif action == 'owner-loss':
                        os.kill(daemon.pid, signal.SIGCONT)
                        daemon.terminate()
                        daemon.wait(timeout=3)
                    elif action == 'restart':
                        daemon = start_daemon()
                else:
                    if action == 'change-now':
                        first.release_name(SERVICE)
                        second.request_name(SERVICE, 4)
                        second.release_name(SERVICE)
                    # Reset service name only when explicitly instructed by the fixture caller.
                    second.release_name(SERVICE)
                    first.request_name(SERVICE, 4)
                state['mode'] = action
                state['calls'] = []
                state['loads'] = 0
                state['value'] = CANARY
                state['content_type'] = 'text/plain; charset=utf-8'
                if 'bytes' in data:
                    state['value'] = bytes(data['bytes'])
                if 'contentType' in data:
                    state['content_type'] = data['contentType']
                output({'configured': True})
                return True

            GLib.io_add_watch(sys.stdin, GLib.IO_IN | GLib.IO_HUP, command)
            signal.signal(signal.SIGTERM, lambda *_: loop.quit())
            output({'environment': env})
            loop.run()
            for obj in objects:
                obj.remove_from_connection()
            # Only fixture-owned synthetic storage is inspected, never personal files.
            for path in root.rglob('*'):
                if path.is_file() and not path.is_symlink():
                    content = path.read_bytes()
                    if CANARY in content or b'SYNTHETIC-rotated' in content:
                        raise RuntimeError('synthetic_plaintext_storage_leak')
            observer.close()
            bus.close()
            second.close()
        finally:
            for child in reversed(children):
                if child.poll() is None:
                    os.killpg(child.pid, signal.SIGTERM)
                try:
                    child.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    os.killpg(child.pid, signal.SIGKILL)
                    child.wait(timeout=3)
    output({'cleaned': not root.exists(), 'reaped': all(p.poll() is not None for p in children)})


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('synthetic_fixture_failed', file=sys.stderr)
        sys.exit(1)
