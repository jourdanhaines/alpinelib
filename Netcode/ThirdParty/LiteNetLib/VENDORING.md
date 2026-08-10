# LiteNetLib (vendored)

| | |
|---|---|
| Upstream | https://github.com/RevenantX/LiteNetLib |
| Tag | `2.1.4` |
| Commit | `4d3de1e93abaead30199bf572f4a3363f854e14b` |
| License | MIT — see `LICENSE.txt` |

Copied verbatim from the upstream `LiteNetLib/` library directory. Samples, tests, benchmarks and the
Unity sample project were not copied.

Two upstream files were dropped because they conflict with how this package builds:

- `LiteNetLib.csproj` — the .NET build compiles these sources through
  `Server/AlpineLib.Netcode/AlpineLib.Netcode.csproj`, which globs all of `Netcode/**/*.cs`.
- `package.json` — a nested UPM manifest inside an existing UPM package confuses the package manager.

`LiteNetLib.asmdef` was kept. It has to stay: LiteNetLib compiles `unsafe` code unconditionally and
references `UnityEngine` behind `UNITY_*` guards (`PausedSocketFix`, `NetDebug`, `LiteNetManager.Socket`),
while `FluxInteractive.AlpineLib.Netcode` is deliberately `noEngineReferences: true` and safe-code only.
Keeping the third-party sources in their own Unity assembly lets both constraints hold at once;
`FluxInteractive.AlpineLib.Netcode` simply references `LiteNetLib`.

Do not hand-edit these sources. To move to a newer release, re-copy the upstream directory, re-apply the
two deletions above, and mint `.meta` files plus `guid-registry.txt` rows for any added file.
