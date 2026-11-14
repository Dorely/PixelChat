## Install

- **Windows 10/11 (x64):** run `PixelChat-Setup-<version>.exe`. The installer is unsigned, so
  SmartScreen may warn: choose **More info → Run anyway**. It adds the `pixelchat` agent CLI to
  your PATH.
- **Debian/Ubuntu (x64):** `sudo apt install ./pixelchat_<version>_amd64.deb`. Start PixelChat from
  the app menu; the `pixelchat` agent CLI is linked into `/usr/bin`.
- **Other Linux (x64):** `chmod +x PixelChat-<version>-x86_64.AppImage` and run it. AppImages need
  FUSE 2 (`libfuse2`), and the agent MCP registration needs the .deb or Windows install.

Saving a Gemini key or signing in to Codex needs OS secure storage; on Linux that is a Secret
Service keyring such as GNOME Keyring or KWallet. `SHA256SUMS.txt` lists the file checksums.
