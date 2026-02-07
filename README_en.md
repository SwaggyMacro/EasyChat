<div align="center">

# EasyChat

[English](README_en.md) | [简体中文](README.md)

[![Downloads](https://img.shields.io/github/downloads/SwaggyMacro/EasyChat/total?style=flat-square&color=blue)](https://github.com/SwaggyMacro/EasyChat/releases)
[![Stars](https://img.shields.io/github/stars/SwaggyMacro/EasyChat?style=flat-square&color=yellow)](https://github.com/SwaggyMacro/EasyChat/stargazers)
[![License](https://img.shields.io/github/license/SwaggyMacro/EasyChat?style=flat-square&color=orange)](https://github.com/SwaggyMacro/EasyChat/blob/master/LICENSE)

</div>

EasyChat is a cross-platform instant translation tool developed based on Avalonia. It is the third refactored version following the "Communication Artifact" (original site [https://f.julym.com](https://f.julym.com)) developed using Easy Language in high school in 2018.
This project aims to provide a smoother and more modern cross-language communication experience.

## ✨ Core Features

The core features of this software focus on solving high-frequency cross-language communication scenarios:

1.  **Screenshot OCR Translation**
    *   Press the shortcut key to select a screen area, automatically recognize text in the image, and quickly translate it into the target language, with results displayed directly as an overlay.
2.  **Input Auto-Translation**
    *   Press the shortcut key in any chat software dialog box to bring up the input window.
    *   Input your native language (e.g., Chinese), and the software automatically translates it into the target language (e.g., English, Japanese, etc.).
    *   After translation is complete, the translation is automatically delivered and sent to the original dialog box, achieving seamless communication.

## 🤖 Translation Engine Support

As the software is an open-source project, users need to configure the API Key for translation services themselves. Currently supports mainstream AI large models and traditional machine translation:

### AI Large Models (Recommended)
Supports custom Prompts, perfectly solving the problem of inaccurate translation of specific domain terms (such as game terms CS2, programming terms, etc.).

*   **SiliconFlow**: [Application Address](https://cloud.siliconflow.cn/i/x8pm79KY)
    *   *Advantages*: Provides various free small-parameter models, and the translation quality and speed fully meet daily use.
*   **iFlow**: [Application Address](https://www.iflow.cn/)
    *   *Advantages*: Provides various large-parameter models, such as Qwen3-Max, suitable for users with higher requirements for translation quality. API limit is 1 QPS, but you can register multiple accounts for polling.
*   **ModelScope**: [Application Address](https://www.modelscope.cn/)
    *   *Advantages*: Provides various large models, with 2000 free calls per day.

### Machine Translation
*   **Baidu Translate**: [Application Address](https://fanyi-api.baidu.com/product/11)
    *   Personal verified users: 1 million free characters/month
    *   Enterprise verified users: 2 million free characters/month
*   **Tencent Translate**: [Application Address](https://cloud.tencent.com/document/product/551)
    *   Free quota: 5 million characters/month

## 🛠️ Tech Stack

This project is built using the modern .NET technology stack, dedicated to future cross-platform support:
*   **Core Framework**: [Avalonia UI](https://avaloniaui.net/) (Preparing for subsequent cross-platform support)
*   **UI Component Library**: [SukiUI](https://github.com/kikipoulet/SukiUI)
*   **OCR Engine**: [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)

## 📖 Usage Tutorial

1.  **Download and Run**:
    *   Download the latest version compressed package from the [Releases](../../releases) page.
    *   Unzip and find `EasyChat.exe` to double-click and run.
2.  **Configure Translation Source**:
    *   Open software settings.
    *   Configure the API Key for any AI large model or machine translation platform.
        ![AddAiModel](./docs/screenshot/HowToUse/AddAiModel.png)
        ![AddAiModel](./docs/screenshot/HowToUse/AddAiModel2.png)
3.  **Set Shortcuts**:
    *   **Basic Hotkeys**: Add global hotkeys for "Screenshot Translation" and "Input Translation" in settings.
        ![SetHotkey](./docs/screenshot/HowToUse/SetHotKey.png)
    *   **Language Switch Hotkey**:
        *   Add a new switching configuration.
        *   Select the configured translation engine.
        *   Set the source language and target language (e.g., Source Language English -> Target Language Chinese).
        *   *Logic Explanation*: Under this configuration, screenshot translation will translate English to Chinese; during input translation, when you input Chinese, the software will translate it to English.
            ![SetHotkey](./docs/screenshot/HowToUse/SetHotKey2.png)
4.  **Start Using**:
    *   **Screenshot Translation**: Press the set screenshot translation hotkey, select the screen area, and wait for the translation result to hover and display.
    *   **Input Translation**: Press the input translation hotkey in the chat window, input native language text, and wait for the translation to automatically send.
    *   **Switch Language**: When you need to switch the translation language, press the set language switch hotkey.


### 📹 Demo Videos

#### Real-time Voice Recognition

https://github.com/user-attachments/assets/f189cb57-383f-47d8-9b15-dafe667eff75

https://github.com/user-attachments/assets/6ab9b6a3-446d-403a-b37a-d49b39c0f9d3

### 💡 Advanced Features: Custom Prompt
Currently, the software supports Large Model Prompt configuration. You can optimize translation results for specific scenarios.
*   *Case*: Add CS2 game prompt words, let AI translate the game term "dinked" to "Headshot", outputting accurate game terms.

## 🚀 Roadmap

The project currently has basic functions perfected, and will purely continue iterative development, planning to add the following functions:

- [x] **Selection Translation**: Select text to translate directly.
- [x] **Dictionary Function**: After screenshot, click on words to view detailed explanations, example sentences, pronunciation, and phonetic symbols.
- [x] **Real-time Voice Translation**: Currently, this function is still under development, requires downloading model files. Those who want to try it first can join the QQ group below and ask in the group.
- [ ] **Fixed Area Translation**: Set specific screen areas, one-key translation (suitable for specific scenarios like Galgames).
- [ ] **Same Color Mask**: After screenshot translation, cover the original text with a background-colored mask and display the translation, providing a more immersive reading experience.

## 🗪 Communication
<img width="234" height="303" alt="image" src="https://github.com/user-attachments/assets/cb1503b9-96a9-496c-93f3-c30d3df2957f" />

## 🤝 Contribution

If you are interested in this project, welcome to submit PRs (Pull Requests) to improve code or add new features.

## ⭐ Support Project

If `EasyChat` is helpful to you, welcome to click the **Star** ⭐ in the upper right corner of the project to support the author! Your support is my biggest motivation for continuous development and maintenance.
