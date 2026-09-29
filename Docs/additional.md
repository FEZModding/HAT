# Additional HAT behaviour

Apart from loading mods, HAT does some additional internal behaviour worth mentioning. Here's a full list:

- `FezLogo` class has been patched in order to draw HAT logo and mod loader tooltip.
- Several methods in `Logger` class have been hooked to override location of debug log files (they're now stored in `%appdata%/FEZ/Debug Logs` directory) and to show an error with stack trace on fatal error.
- FEZ's text lookups check merged `ModText` resources before the original text resources. HAT's own menu labels use the same keys, so mods can localize them.
- `Menu Base` class' `Initialize` method has a hook which adds an additional `MODS` menu, where you can preview a list of currently installed modifications.
