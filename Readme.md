# TConvert ![AppIcon](http://i.imgur.com/5WPwZ3W.png)

[![最新版本](https://img.shields.io/github/release/trigger-death/TConvert.svg?style=flat&label=version)](https://github.com/trigger-death/TConvert/releases/latest)
[![最新发布日期](https://img.shields.io/github/release-date-pre/trigger-death/TConvert.svg?style=flat&label=released)](https://github.com/trigger-death/TConvert/releases/latest)
[![总下载量](https://img.shields.io/github/downloads/trigger-death/TConvert/total.svg?style=flat)](https://github.com/trigger-death/TConvert/releases)
[![创建日期](https://img.shields.io/badge/created-august%202017-A642FF.svg?style=flat)](https://github.com/trigger-death/TConvert/commit/81d10e01975c1974f73ee90089fa30d85e71370e)
[![Terraria 论坛](https://img.shields.io/badge/terraria-forums-28A828.svg?style=flat)](https://forums.terraria.org/index.php?threads/61706/)
[![Discord](https://img.shields.io/discord/436949335947870238.svg?style=flat&logo=discord&label=chat&colorB=7389DC&link=https://discord.gg/vB7jUbY)](https://discord.gg/vB7jUbY)

一个管理 Terraria 内容资源的组合工具。可以提取、转换、备份和恢复。TExtract 的非官方续集。

![窗口预览](https://i.imgur.com/oTuVrGQ.png)

### [Wiki](https://github.com/trigger-death/TConvert/wiki) | [Credits](https://github.com/trigger-death/TConvert/wiki/Credits) | [图片相册](https://imgur.com/a/QaoPd)

### [![下载 TConvert](http://i.imgur.com/4BGRFF0.png)](https://github.com/trigger-death/TConvert/releases/latest)

## 关于

* **作者:** Robert Jordan
* **版本:** 1.0.2.1
* **语言:** C#, WPF

## 运行要求
* .NET Framework 4.5.2 | [离线安装包](https://www.microsoft.com/en-us/download/details.aspx?id=42642) | [网页安装包](https://www.microsoft.com/en-us/download/details.aspx?id=42643)
* Windows 7 或更高版本

## 从源码构建
* 使用配置 *WinDebug* 或 *WinRelease* 来构建 UI 版本。
* 使用配置 *ConDebug* 或 *ConRelease* 来构建纯控制台版本。

## 功能
* 从 Terraria 的 Xnb 文件中提取图像、声音和字体资源，并从 Terraria 的 Xwb wave bank 中提取歌曲。
* 将图像和声音转换回 Xnb 格式，并将其复制到内容目录中。
* 备份和恢复您的内容文件夹，以便在需要撤销更改时使用。（类似于文件复制器）
* 运行脚本，提供更多控制，允许您在转换或提取文件时指定文件的位置。
* 将文件拖放到窗口中以自动处理它们。
* 提供命令行支持，可通过 Windows Shell 或命令提示符使用。

## 关于 Xnb 格式

我了解 Xnb 格式的所有内容，特别是读取精灵字体的部分，都来自于 [此页面上的文档](http://xbox.create.msdn.com/en-us/sample/xnb_format)。如果那个链接失效，您可以访问 [这个镜像](http://www.mediafire.com/file/pf5dqw5dmup1msa/XNA_XNB_Format.zip)，因为微软的旧链接通常会失效。
