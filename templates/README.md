# K-Desktop assistant templates

`dotnet new` templates for [K-Desktop](https://github.com/Kobayashi82/K-Desktop) assistants: characters that live on top
of your windows, talk with their own voice, think with a local AI and run your K-Desktop commands with visible gestures.

```powershell
dotnet new install KDesktop.Assistant.Templates
dotnet new kdassistant -n MyAssistant --authorName "Me"
cd MyAssistant
dotnet build          # → bin\Debug\…\myassistant-1.0.0.kai
```

The new project compiles against the [KDesktop.Assistant.Sdk](https://www.nuget.org/packages/KDesktop.Assistant.Sdk)
package. It is the smallest complete assistant with code: it draws itself, has a click area, talks, reacts, changes
colour with each state and shows a rainbow in its showcase.

| Option | |
|---|---|
| `--authorName` | Author shown in K-Desktop when the assistant is installed |
| `--sdkVersion` | Version of the SDK package (the one this template was made with by default) |
| `--sdk` | Only to build against a clone of K-Desktop: the folder of its `src\KDesktop.Assistant.Sdk` |

Try it without installing with `KDesktop.exe --assistant-dev "<project folder>"`. The guide:
[Making assistants](https://kobayashi82.github.io/K-Desktop/en/sdk/index.html).
