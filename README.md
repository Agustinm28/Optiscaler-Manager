# ✦ OptiScaler Client

[![GitHub Release](https://img.shields.io/github/v/release/Agustinm28/Optiscaler-Client?style=flat-square&color=8A2BE2)](https://github.com/Agustinm28/Optiscaler-Client/releases/tag/OptiscalerClient-1.0.3)
[![License: GPL](https://img.shields.io/badge/License-GPL-yellow.svg?style=flat-square)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows)

> **⚠️ Disclaimer:** This is **not** an official OptiScaler project. I am not affiliated with the OptiScaler team. This is a personal project developed without any commercial purpose. Anyone is free to try and use this software at their own risk.

**OptiScaler Client** is a modern, high-performance desktop utility designed to simplify the installation, management, and update of the **OptiScaler** mod across your entire game library. Built with **C#** and **Avalonia UI**.

---

## Screenshots

* Main window

<img width="1266" height="752" alt="1 0 3_A" src="https://github.com/user-attachments/assets/09b0706a-e047-485c-9ac7-0847d80a3fc5" />

* Game management

<img width="1266" height="751" alt="1 0 3_B" src="https://github.com/user-attachments/assets/b4c86a32-13df-4ae2-921e-b89aacb7dfa1" />

* Main window after installation

<img width="1266" height="752" alt="1 0 3_C" src="https://github.com/user-attachments/assets/e88307c8-13af-4401-90a0-a6daaf8a4977" />

---

## 🚀 Key Features

*   **Auto-Scanner**: Deeply scans Steam, Epic Games, GOG, EA, Ubisoft, Battle.net, and Xbox libraries to find your games instantly.
*   **One-Click Install**: Automatically downloads and configures the latest OptiScaler versions for specific titles.
*   **Component Control**: Manage additional tools like **Fakenvapi** (for AMD/Intel GPUs), **Nukem's DLSSG-to-FSR3** mod, and **FSR 4 INT8** injection for non-RDNA 4 GPUs.
*   **Cache Management**: Keep your storage clean by managing different downloaded mod versions.
*   **Localization**: Full support for Multiple languages (English, Spanish & Brazilian Portuguese).
*   **Native Performance**: Fully self-contained, single-file executable for a zero-footprint installation.

---

## 📖 Usage Guide

Follow these simple steps to enhance your games:

1.  **Find your games**: Click the **"Scan Games"** button to automatically detect installed titles. You can also manage scan sources or add custom folders for scanning in the **Settings** menu. For standalone or DRM-free games, use the **"Add Manually"** button.
2.  **Select a Game**: Click the **"Manage"** button next to any game in your list to enter the management dashboard.
3.  **Install OptiScaler**: Choose your desired version and click **"Auto Install"**. If the game uses a non-standard structure, use **"Manual Install"** to select the executable yourself.
4.  **Launch & Tweak**: Start your game normally. Once inside, press the **`Insert`** key to open the OptiScaler menu and fine-tune your upscaling settings in real-time.

---

## 🛠️ Installation & Requirements

1.  Download the latest version from the [Releases](https://github.com/Agustinm28/Optiscaler-Client/releases) page.
2.  Run `OptiscalerClient.exe`.
3.  **Requirements**: Windows 10/11 is required. The app is self-contained, so no external .NET runtime installation is needed.

---

## 🛡️ Security & Antivirus False Positives

**Is this software safe?**

Yes, OptiScaler Client is completely safe and open-source. However, some antivirus programs may flag it as suspicious due to **false positive detections**.

### Why does this happen?

- **File Downloads**: The app downloads `.zip` and `.dll` files from GitHub (OptiScaler, Fakenvapi, NukemFG)
- **Heuristic Detection**: Antivirus software may flag download behavior as "potentially unwanted"
- **Unsigned Binary**: The executable is not digitally signed (code signing certificates cost $100-300/year)

### Common False Positives

- **Zillya**: `Downloader.MLoki.Win64.10` - Known for aggressive heuristics
- **Other AVs**: May show generic "downloader" or "trojan" warnings

### What you can do

1. **Verify the Source**: Download only from official [GitHub Releases](https://github.com/Agustinm28/Optiscaler-Client/releases)
2. **Check VirusTotal**: Upload the file to [VirusTotal.com](https://www.virustotal.com) - most reputable AVs will show clean
3. **Review the Code**: This is open-source - you can inspect all code before running
4. **Add Exception**: Whitelist `OptiscalerClient.exe` in your antivirus settings

### Transparency

All downloads are from official sources:
- OptiScaler: `github.com/optiscaler/OptiScaler`
- Fakenvapi: `github.com/optiscaler/fakenvapi`
- NukemFG: `github.com/Nukem9/dlssg-to-fsr3`

The application **never** collects personal data, connects to third-party servers, or performs any malicious actions. All source code is available for audit.

---

## 🤝 Contributing

We welcome contributions! If you'd like to improve OptiScaler Client:

1.  **Fork** the project.
2.  Create your **Feature Branch** (`git checkout -b feature/AmazingFeature`).
3.  **Commit** your changes (`git commit -m 'Add some AmazingFeature'`).
4.  **Push** to the branch (`git push origin feature/AmazingFeature`).
5.  Open a **Pull Request**.

---

## 📄 License & Acknowledgments

*   Distributed under the **GPL-3.0 License**. See `LICENSE` for more information.
*   **Special thanks and deep respect to the OptiScaler development team** for creating and maintaining this incredible software that enhances gaming experiences for countless users worldwide.
*   **[OptiScaler](https://github.com/optiscaler/OptiScaler)**: The core technology that makes this possible.
*   **[fakenvapi](https://github.com/optiscaler/fakenvapi)**: Essential compatibility layer developed by the OptiScaler team.
*   **[NukemFG](https://github.com/Nukem9/dlssg-to-fsr3)**: Frame Generation bridge by Nukem.

This client application is merely a frontend interface to help users more easily manage and install the amazing work done by the OptiScaler team.

---

<p align="center">
  Developed with ❤️
</p>
