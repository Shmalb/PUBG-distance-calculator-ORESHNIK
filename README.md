# ORESHNIK — PUBG Distance Calculator

How it works
- The app provides a screen overlay to mark two points and measure the pixel distance between them.
- You calibrate pixels to meters by specifying how many pixels correspond to 100 meters (manual input) or by selecting two points in-game that are 100 m apart (automatic calibration).
- After calibration the overlay converts pixel distance to meters and draws markers and a measuring line.

Quick usage
- Calibration:
  - Enter the pixel count that equals 100 m in the "Pixels" field and click "Apply", or
  - Click "Manual calibration" and press the calibration hotkey (default `F8`) on two in-game points that are 100 m apart.
- Measurement:
  - Press the measure hotkey (default `F9`) on the first point, then on the second point. The app will display the distance in meters and draw a line on the overlay.
- Clear:
  - Press the clear hotkey (default `F10`) or click the Clear button to remove markers.
- Change hotkeys:
  - Click a hotkey button in the UI, then press the desired key. Save settings with the "Save settings" button.

Notes
- For correct overlay alignment the game must be running in Windowed or Borderless (Windowed Fullscreen) mode. Fullscreen exclusive mode may prevent overlay or input capture from working correctly.
- Some games require running the app as Administrator to draw overlay or capture input.

Build and release
- The project targets .NET Framework 4.7.2. Build in Visual Studio using the `Release` configuration.
- The built executable is typically found at `ORESHNIK\bin\Release\ORESHNIK.exe`.

If you need a more detailed user guide or an English/expanded README, request the additions.
