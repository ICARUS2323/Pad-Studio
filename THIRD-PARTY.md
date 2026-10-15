# Interception

Pad Studio uses the unmodified x64 Interception library and command-line driver installer, release v1.0.1 by Francisco Lopes.

- Project and corresponding library source: https://github.com/oblitum/Interception/tree/v1.0.1
- Release archive: https://github.com/oblitum/Interception/releases/tag/v1.0.1
- License included in licenses/Interception-LGPL.txt. The library is dynamically linked and can be replaced by a compatible build. No reverse-engineering restriction is imposed for debugging modifications to this library.
- The upstream project permits non-commercial usage under its LGPL terms. Commercial distribution requires reviewing upstream commercial licensing.

The driver is not installed when Pad Studio is launched. Installation and removal are separate, explicit administrator actions; reboot is required. Upstream documents testing through Windows 10. Compatibility with this Windows 11 machine must be verified after installation. Pad Studio does not disable Windows security features.
