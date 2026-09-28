# Contributing to ControlSpace

Keep model/language/simulation code independent of the Uno application. Add a focused regression for each behavioral change and update the capability matrix when a compatibility boundary changes. Do not describe unimplemented controls, protocols or file formats as supported.

Run `npm test` and the portable C# runner (`dotnet run --project tests/ControlSpace.Tests -c Release -- --fixtures tests/fixtures`). Build both Uno target frameworks, exercise the browser app, and attach screenshots that state whether they show Uno or the JavaScript prototype. Changes affecting shared behavior should extend `tests/fixtures` and pass both engines. Do not treat fixture parity as full IEC/S7 conformance.

Use small, reviewable commits. Avoid adding generated build output, credentials, controller backups, firmware, manufacturer assets or font files. Keep project imports bounded and reject unsupported syntax explicitly. Any future hardware transport must be a separate, opt-in package with a written threat model, read/write authorization and a non-simulation UI boundary; this repository currently performs no hardware I/O.

The code is MIT licensed. Contributions must be original or appropriately licensed and attributed in THIRD-PARTY-NOTICES.md.
