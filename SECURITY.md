# Security and operational boundaries

ControlSpace is an experimental offline engineering editor and in-process simulator. It is not a PLC, commissioning tool, emergency stop, safety controller or hard-real-time runtime. Never use its outputs to control equipment. The application does not open industrial network connections or download code to controllers.

Project data is untrusted. Imports use an 8 MiB limit, schema/version checks, duplicate-key detection, operand/type validation and bounded language parsing. The SCL subset is interpreted and cannot evaluate JavaScript, C# or arbitrary operating-system commands. The browser UI escapes project text before displaying it in HTML. Defensive checks are not a substitute for independent security review or resource-exhaustion testing.

Local recovery is not encrypted, authenticated, backed up or synchronized. Browser storage may be unavailable, evicted or lost. Export important projects. Do not store credentials, production equipment secrets or proprietary control-system backups in the sample workspace.

Simulation STOP clears output-image tags and virtual forces. An arithmetic/type fault rolls back the value-image transaction, clears output tags and requires reset. These are local preview conventions, not certified fail-safe behavior. A machine's emergency-stop circuit must never depend on this software.

No external telemetry, API keys, hosted collaboration service, authentication or hardware gateway is implemented. Future online protocols require a separate design and security review before being added. Report ordinary defects through repository issues after publication; avoid disclosing credentials or sensitive production project files in public issues.
