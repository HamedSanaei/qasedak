using System.Runtime.CompilerServices;

// Transport internals (SOAP client boundary, wire records) stay hidden from Application
// code but are visible to the deterministic test suites that exercise them.
[assembly: InternalsVisibleTo("Qasedak.Modules.Billing.UnitTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
[assembly: InternalsVisibleTo("Qasedak.Api.IntegrationTests")]
