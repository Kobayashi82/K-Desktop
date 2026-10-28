# Pendientes K-Desktop (5.0.311)

## Lo que tenía en mente

- [ ] Probar todas las **acciones**
- [ ] Probar todos los **eventos**
- [ ] Probar todas las **macros**
- [ ] Probar **K-Board**
- [ ] Probar todas las **herramientas** (de todos los tipos)
- [ ] Actualizar la **web / APK**
- [ ] Actualizar los **clientes UDP**
- [ ] Revisar los asistentes **Lander**, **Kumo** y **Kobii**

## Además

### Interfaz
- [ ] **Tema claro** página por página, incluidas las ventanas sueltas: QuickMenu, OSD, círculo de cuenta atrás, lanzador, menús de bandeja y panel del asistente
- [ ] Cambiar de tema en marcha en los dos sentidos (claro → oscuro y oscuro → claro)
- [ ] **Bienvenida**: probarla con un perfil limpio y con `--welcome`; que cada paso se aplique al momento y que los logros salgan al cerrarla

### Asistentes y voz
- [ ] **Órdenes programadas**: `schedule`, `list_scheduled` y `cancel_scheduled`, cómo se aprueban, qué pasa si el PC estaba apagado o dormido a esa hora y que «apagar» va siempre lo último
- [ ] **Voz**: que la RAM se libere al desactivarla (Parakeet y EmbeddingGemma) y la palabra de activación
- [ ] «Cancelar» en los 6 idiomas
- [ ] Panel nuevo de **sonidos**, con los sonidos antiguos pasados a los de Windows
- [ ] **Servidor MCP**: que otro programa use las herramientas compartidas y que salga en el Registro y en Logros

### Documentación
- [ ] Web (`site/`): solo tiene `en` y `es`, mientras que la app está en 6 idiomas. Decidir si hace falta más
- [ ] Volver a grabar el **vídeo tutorial** y las **capturas** (`docs/videos`, `docs/tutorial/captures`) con `tools/tutorial`
- [ ] `README.md` y `README.es.md`

### Publicar
- [ ] Volver a empaquetar y firmar los `.kai` (KaiSign) y subirlos al release `assistants` que apunta `catalog.json`
- [ ] Si ha cambiado el SDK: el paquete NuGet (`publish-nuget.ps1`), la plantilla `templates/kdassistant` y el ejemplo `samples/CharacterAssistant`
- [ ] Paquete de **actualización automática** (`make-update.ps1`)
- [ ] Que una instalación anterior se actualice bien a la 5.0.311 y conserve sus ajustes

### Otros
- [ ] **Importador** (`importer/`): si sigue en uso, comprobar que lo que importa encaja con los formatos actuales
- [ ] Pasar las **pruebas automáticas** de `tests/` (Core y Assistant) antes de sacar la versión
