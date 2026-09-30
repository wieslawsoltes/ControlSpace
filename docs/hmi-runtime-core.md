# Reusable HMI runtime APIs

The former library-only candidate is now integrated with the complete Uno operator UI. [HMI runtime](hmi-runtime.md) documents the canonical immutable `HmiObject.Runtime` / `HmiRuntimeOptions` model, shared `HmiRuntimeRules`, controller writes, screen navigation and numeric entry. The unpublished alternative `Button`/`Numeric` option members were consolidated into `Runtime` before merge; this is not a migration from a released format.

`HmiNavigationSession` remains available for hosts that need only a 64-entry, runtime-only navigation stack. `Activate(project, current, target)` validates both screen IDs before pushing; self-navigation is a no-op. `Back` skips removed screens. `Clear` resets history. Hosts must call Clear on project replacement, including replacement with the same project ID.

`HmiNumericInputSession` remains a one-shot numeric API bound to the exact project reference, compiled content, controller instance and lifecycle epoch. It uses the same canonical Runtime options and parser as the Uno numeric-entry dialog. `Commit(project, controller, text)` rejects stale or completed sessions, but invalid text can be corrected before a successful commit. Hosts must discard this standalone entry on screen navigation; the higher-level `HmiRuntimeSession` does this automatically.

`VirtualPlc.SetOperatorValue` retains its numeric-only input/marker contract and delegates to `WriteHmiValue`, which also supports typed Boolean actions. Neither API creates a force or edits project initial values. Output-image, forced, faulted and out-of-range writes are rejected. These single-threaded APIs operate only on the in-process simulator and do not implement hardware transport, authentication or safety functions.
