using System.Runtime.CompilerServices;

// Allow the test project to access internal types
[assembly: InternalsVisibleTo("XrmEmulator.Tests")]

// Allow Moq's Castle.DynamicProxy to create mocks of internal types
// Since XrmEmulator is not strongly-named, we don't need the public key
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
