# AzireVPN for Windows

This is a simple client for AzireVPN for Windows, which brings up WireGuard® tunnels using the [embeddable-dll-service](https://git.zx2c4.com/wireguard-windows/about/embeddable-dll-service/README.md).

## Disclaimer

Currently this code is not production ready and is in "example stage" only, so you probably should not use it. See the initial commit (and possibly subsequent ones) for more details on what's missing. Presently, this is mostly an example project to demonstrate use of the [embeddable-dll-service](https://git.zx2c4.com/wireguard-windows/about/embeddable-dll-service/README.md).

## Building

The code in this repository can be built in Visual Studio 2019 by opening the .sln and pressing build. However, it requires `tunnel.dll` to be present in the run directory. That can be built by:

```batch
> git clone https://git.zx2c4.com/wireguard-windows
> cd wireguard-windows\embeddable-dll-service
> .\build.bat
```

In addition, `tunnel.dll` requires `wintun.dll`, which can be downloaded from [wintun.net](https://www.wintun.net).

## License

This project is released under the GPLv2.

---

"WireGuard" is a registered trademark of Jason A. Donenfeld.
