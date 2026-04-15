# Aerochat

A custom client for Discord that resembles Windows Live Messenger 2009.

## About Updated

Since the main project is now pretty much dead and abandonned, I decided to fork it and keep it updated with new features and try to keep it as stable as I can. I cannot guarantee much as I am far from being a good programmer lmao but at least for those who were interested in the original project and wanted to keep using it, there you go. Feel free to contribute!!!

## Windows Vista compatibility

Updated Aerochat is TECHNICALLY compatible with Windows Vista. It can be installed on Vista, but you probably won't be able to log in.
The main reason being that Windows Vista does not come with TLS 1.2 by default. This is however achievable by fully updating the OS with [Legacy Update](https://legacyupdate.net/) and [following this guide](https://johnhaller.com/useful-stuff/enable-tls-1.1-and-1.2-on-windows-vista).

After doing so, either it'll work, or it still won't.

## Download

To download Aerochat, please click the links in the "Releases" section on the right of this page, or visit the following link:

### [View releases](https://github.com/Maxokie/Updated-Aerochat/releases)

## Frequently-asked questions

Please see our dedicated page for frequently-asked questions and help.

### [View frequently-asked questions](https://github.com/Maxokie/Updated-Aerochat/wiki/Frequently%E2%80%90asked-questions)

## Building

In order to build Aerochat from source, you will need:

- [Visual Studio 2022 (with the .NET Desktop Development workload)](https://visualstudio.microsoft.com)

Aerochat cannot be built as `AnyCPU` due to depending on native code. You must set the build architecture to `x64` before you can build.

## For any questions (that aren't already answered in the faq), please add me on Discord: maxokie