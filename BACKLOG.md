# Deferred proposals

## Contents

- [Deferred proposals](#deferred-proposals)

Inclusion here does not authorize implementation. Agree on scope and acceptance criteria before starting; preserve [SECURITY.md](SECURITY.md). Current implementation and acceptance work belong in [PLAN.md](PLAN.md).

| Proposal | Scope still to decide |
| --- | --- |
| Server-side attachment interpretation | Text extraction, PDF rendering, OCR and encrypted-document handling. Current downloads delegate interpretation to the client's tools. |
| Bulk downloads or remote file delivery | Product need, storage/retention, transport and security boundaries; current downloads save one attachment to a private local directory. |

[Search pagination](docs/SEARCH-PAGINATION.md) and [attachment downloads](docs/ATTACHMENT-DOWNLOADS.md) are implemented. Their remaining live/client acceptance is tracked in PLAN rather than as future features.

[Linux keyring support](docs/KEYRING.md) is implemented with isolated native/GNOME tests; live Bridge acceptance remains separately gated in PLAN. Encrypted systemd credentials, unattended keyring unlock and non-GNOME provider compatibility remain outside that feature's scope.
