# One-shot administrator script runner

Build with `dotnet build tools/AdminScriptRunner -c Release`.

Review a PowerShell script and compute its SHA-256 with `Get-FileHash`.
Launch the built executable with Windows `RunAs`, passing the full script path,
the expected SHA-256, a new JSON result path, and any script arguments.
Windows displays UAC. The runner verifies the hash, prevents changes to the script
while it runs, executes one noninteractive PowerShell process, records its output
and exit code, and exits. It has no listener, startup registration, or persistent service.

Only the selected script's bytes are verified. Review its arguments and any files,
modules, or programs it loads as well. This is an investigation tool, not part of
the shipped PreySense application.

Run each additional script through its own reviewed one-shot invocation.
