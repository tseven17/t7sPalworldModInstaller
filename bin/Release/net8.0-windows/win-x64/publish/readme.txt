t7’s Palworld Mod-Installer
===========================

Version    : 1.2.2
Author     : t7
Date       : 07-Jun-2025
Repo       : https://github.com/<your-repo>   ← replace with actual link
Contact    : xDREAM Discord  (https://discord.xdreamserver.com)

--------------------------------------------------------------------------------
Overview
--------------------------------------------------------------------------------
A WinForms utility that backs up, installs and verifies Palworld mods.  
• GUI themed for dark “gamer” style  
• Safe backup (copy-then-delete, never moves locked folders)  
• Detailed integrity check with easy-to-follow repair steps  
• No external dependencies except .NET 8 Desktop Runtime

--------------------------------------------------------------------------------
System requirements
--------------------------------------------------------------------------------
Windows 10/11 x64  
.NET Desktop Runtime 8.0  (https://dotnet.microsoft.com/download)

--------------------------------------------------------------------------------
How to build from source
--------------------------------------------------------------------------------
git clone <repo> 
cd ModInstaller
dotnet publish -c Release -r win-x64 ^
               -p:PublishSingleFile=false -p:SelfContained=false

--------------------------------------------------------------------------------
How this binary was produced
--------------------------------------------------------------------------------
Commit        : <git commit hash>                ← fill in
Build machine : Windows 11 22H2, VS 2022 17.10  
Build cmd     : dotnet publish -c Release -r win-x64 -p:PublishSingleFile=false -p:SelfContained=false
Sign cmd      : signtool sign /fd SHA256 /t http://timestamp.digicert.com /a ModInstallerApp.exe

--------------------------------------------------------------------------------
Changelog
--------------------------------------------------------------------------------
v1.2.2 • Backup is copy-then-delete (no “move” heuristics)
       • explorer.exe started via UseShellExecute=true
       • Optional process-kill prompt

v1.2.1 • Hardened backup, detailed instructions
v1.2.0 • Added Discord links and Readme/About dialog
v1.1.0 • Initial themed UI release

--------------------------------------------------------------------------------
Usage
--------------------------------------------------------------------------------
1. Launch ModInstallerApp.exe
2. Click **Browse…** and select your Palworld.exe
3. Use **Backup Mods** before changing anything
4. Use **Install Modpack** to apply a ZIP that contains a top-level “Pal” folder
5. **Verify Integrity** checks for missing/changed files
6. **Restore Backup** rolls back to a previous backup snapshot

--------------------------------------------------------------------------------
Checksums
--------------------------------------------------------------------------------
sha256 hashes for every file in this ZIP are stored in checksums.sha256  
Generate/update via PowerShell:  
  Get-FileHash * -Algorithm SHA256 | Format-Table -HideTableHeaders Path,Hash |
    Out-File checksums.sha256

--------------------------------------------------------------------------------
License
--------------------------------------------------------------------------------
This project is released under the MIT License (see license.txt).

Uploading to Nexus-Mods? Ensure the EXE remains signed and the ZIP structure:
  /PalworldModInstaller_1.2.2/
      ModInstallerApp.exe   (signed)
      *.dll
      readme.txt
      license.txt
      checksums.sha256
