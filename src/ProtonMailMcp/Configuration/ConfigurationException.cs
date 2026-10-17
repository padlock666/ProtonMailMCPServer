namespace ProtonMailMcp.Configuration;

// Fixed diagnostics only. Never retain an inner exception containing paths or content.
internal sealed class ConfigurationException(string code) : Exception(code);
