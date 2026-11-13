# Gurux.DLMS.DeviceAccess.Net

A .NET library for communicating with DLMS/COSEM meters. Supports .NET 9, .NET 10 and .NET 11.

## Features

- Asynchronous connection management with cancellation support.
- Read and write attributes, invoke methods and read profile data.
- Certificate generation, import and export.
- Firmware transfer, verification and activation.
- Application-controlled transport and logging.

## Installation

```sh
dotnet add package Gurux.DLMS.DeviceAccess
```

Your application supplies an `IGXAsyncMedia` transport, such as Gurux.Net for TCP/IP.
Concrete transport packages are installed separately.

## Usage

Create a `GXDLMSReader` with a secure DLMS client, your transport and
`GXDLMSReaderOptions`. Connect using `ConnectAsync`, then use `ReadAsync`,
`WriteAsync` or `MethodAsync`. Dispose the reader when finished.

See the [usage guide](Development/README.md) for examples and configuration.

## License

Available under [GPL-2.0-only](LICENSE) or a separate commercial license from Gurux Ltd.
See the [commercial license notice](LICENSES/LicenseRef-Gurux-Commercial.txt).

More information: [gurux.fi](https://www.gurux.fi/).
