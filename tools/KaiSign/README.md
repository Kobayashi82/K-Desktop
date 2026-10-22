# kaisign

Firma los paquetes de asistentes (`.kai`) con tu propia clave, sin certificados de pago. K-Desktop marca como
**verificados** los firmados con una clave de `src/KDesktop.Core/Assistant/Packages/trusted-publishers.json`.

```powershell
dotnet run --project tools\KaiSign -- keygen "Kobayashi82"       # crea la clave (una vez)
dotnet run --project tools\KaiSign -- sign ruta\al\paquete.kai    # firma (sustituye una firma anterior)
dotnet run --project tools\KaiSign -- verify ruta\al\paquete.kai  # lo que diría K-Desktop
dotnet run --project tools\KaiSign -- public                      # la clave pública, para la lista
dotnet run --project tools\KaiSign -- export copia.pem "contraseña"
dotnet run --project tools\KaiSign -- import copia.pem "contraseña" "Kobayashi82"
```

- La clave privada está en `%LOCALAPPDATA%\K-Desktop\Signing\key.dpapi`, cifrada con DPAPI: solo la abre tu usuario de
  Windows en este PC. **Haz una copia con `export`** y guárdala aparte: si se pierde, lo nuevo que firmes con otra clave
  ya no será «verificado» hasta cambiar la lista (y las actualizaciones avisarán de que cambió quién firma).
- Firma lo último, justo antes de publicar: cualquier cambio al paquete después rompe la firma y K-Desktop no lo instala.
- Detalles del formato: `PackageSignature` en `src/KDesktop.Core/Assistant/Packages/PackageSignature.cs`.
