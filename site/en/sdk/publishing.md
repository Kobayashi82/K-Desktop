# Signing and publishing

## Signing the package

A signed package carries `signature.json` with the SHA-256 of **all** its files, who publishes it, its public key (ECDSA
P-256) and the signature. Nobody has to be paid: the key is yours. K-Desktop checks it when it opens the install card:

| What it finds | What it shows |
|---|---|
| A good signature by a key on its list of trusted publishers | **Verified · name**, also in *Settings → Assistants* |
| A good signature by another key | «Signed by "name"», and the usual warning: that name is only what the package says |
| No signature | «Not signed» and the usual warning |
| A signature that does not match (a file changed, added or removed after signing) | **It is not installed** |

If the installed version was signed and the new one is not signed by the same key, the card warns in red.

Make your key once with `tools/KaiSign` (in the K-Desktop repository); from then on **every build signs the `.kai`** by
itself, right after packing it:

```powershell
dotnet run --project tools\KaiSign -- keygen "Your name"      # once: the key stays in your Windows user (DPAPI)
dotnet run --project tools\KaiSign -- export copy.pem          # a copy of the key with a password, to keep elsewhere
dotnet run --project tools\KaiSign -- verify myassistant.kai   # what K-Desktop would say
```

| Build property | |
|---|---|
| (nothing) | Signed if your Windows user has a key; if not, a message says it is unsigned |
| `-p:AssistantSign=false` | Never signed |
| `-p:AssistantSign=true` | An error if it cannot be signed (for the builds you publish) |
| `KaiSignProject` / `KaiSignExe` | Where kaisign is: its `.csproj` (built from the sources) or a built `kaisign.exe` |

A package signed by hand (`kaisign sign myassistant.kai`) works the same. Sign **last**: any change to the package
afterwards breaks the signature. For your packages to show as *Verified*, your public key (`kaisign public`) has to be on
K-Desktop's list.

## New versions

If the manifest says where the latest package always is, K-Desktop looks for new versions together with its own (at start,
every 6 hours and with *Check now* in *Settings → Updates*) and **offers** them: a notice, a bar in the window, the
assistant's row in *Settings → Assistants* and *Settings → Updates*, with *Update…* (downloads it and opens its card) and
*Skip this version*. It never updates an assistant without asking.

```json
"updates": "https://github.com/me/my-assistants/releases/latest/download/myassistant.kai",
"changes": { "en": "Jumps higher and talks faster.", "es": "Salta más alto y habla más rápido." }
```

- `updates` is a fixed address that always gives the latest `.kai` (on GitHub, the `releases/latest/download/…` one).
  https; `http://localhost` and `http://127.0.0.1` work for testing with your own server.
- To check it, K-Desktop **only reads the `assistant.json`** of the published package, with range requests (a few KB); a
  server that does not support them sends the whole package every time.
- It is offered if the version is higher and the id is the same. If it needs a newer SDK than that K-Desktop has, the user
  is told to update K-Desktop first.
- `changes` (optional, in one or several languages) is what that version brings: it shows in the notice and on the card.

## Putting it in a catalog

*Settings → Assistant module → Catalog* shows the assistants of the catalogs it reads (K-Desktop's and any the user adds)
with *Install…* and *Update…*. A catalog is a JSON published at a fixed address
([schema](https://github.com/Kobayashi82/K-Desktop/blob/main/docs/assistant/schemas/catalog.schema.json)) that only says
where each package is:

```json
{
  "catalog": 1,
  "assistants": [
    { "id": "myassistant", "package": "https://github.com/me/my-assistants/releases/latest/download/myassistant.kai" }
  ]
}
```

- `package` is the same address as `updates`: the one that always gives the latest `.kai`.
- Of each package K-Desktop only reads `assistant.json`, the icon and the clips the user opens, by ranges: the name,
  description, version, author and whether it works with that K-Desktop come from the package itself. If the `id` does
  not match the package's, it is not offered.
- Being in a catalog does not make a package trustworthy: the card says whether it is **verified** and what it can do
  before installing it.
