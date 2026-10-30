# Calendario y correo: notas de la implementación

Notas de las versiones 5.0.415 a 5.0.422 (planes `calendario-plan.md` y `correo-plan.md`): qué se decidió donde el
plan dejaba la pregunta abierta, qué no se ha podido probar y cómo configurarlo.

## Decisiones

| Pregunta | Decisión | Por qué |
|---|---|---|
| ¿Biblioteca de iCalendar o propia? | Propia (`Calendar/Recurrence.cs`, `IcsFormat.cs`) | RRULE con BYDAY ordinales, BYMONTHDAY negativo, BYSETPOS, COUNT/UNTIL, EXDATE y RECURRENCE-ID caben en poco código, sin dependencias y con tests; las zonas se resuelven con `TimeZoneInfo` (IANA y Windows) |
| ¿Dónde se guarda? | JSON en `%APPDATA%\K-Desktop 5\Calendar` (propio, cachés de suscripciones y de cuentas, cola sin conexión) | Igual que el resto de la configuración; sin SQLite |
| Cliente de Google | Cada usuario pone su Client ID y secreto (guía en Opciones › Cuentas) | Verificar una app con permisos de Calendar y Gmail exige una auditoría; sin verificar, en «En producción» vale para uno mismo |
| Cliente de Microsoft | `OAuthProvider.MicrosoftBuiltInClientId` está vacío: se pide el Id. de una aplicación de Entra ID | Hay que registrar la de K-Desktop (cliente público, `common`, redirección `http://localhost`) y poner su Id. en esa constante; entonces el campo pasa a ser opcional |
| Tokens | El de refresco cifrado con DPAPI; el de acceso solo en memoria; nunca salen al proceso de los asistentes | Las herramientas piden al calendario y al correo de K-Desktop por la tubería |
| Avisos de citas de Google y Outlook | Apagados por defecto (`AccountReminders`) | Esas cuentas ya avisan en el móvil y en Outlook; se encienden en Opciones › Calendarios |
| ¿Puede enviar el asistente? | Sí (`AssistantMaySend`); `mail_send` pregunta siempre salvo que se elija «Sin preguntar» en Asistente › Herramientas (5.0.424) | Es la petición más habitual («contesta a Ana que…»); tras leer un correo pregunta igualmente |
| Contenido de los correos | `mail_list` y `mail_read` marcan el turno como «leído de fuera»: desde ahí, cualquier herramienta que no sea de solo lectura pregunta (`tools.askAfterMail`) y el texto llega envuelto como no fiable | Defensa contra inyección: lo que diga un correo no es una orden |
| Marcar como leído | No (`MarkReadByAssistant` apagado) | Que el asistente lea no debe cambiar el estado del buzón |
| Avisos de correo nuevo | «Importantes» por defecto: los de los remitentes o con las palabras en el asunto de Opciones › Correo (se puede elegir «Todos» o «Ninguno» por cuenta) | Avisar de todo es ruido en cuanto hay publicidad |
| Días que se guardan | 30 (`KeepDays`): las cabeceras de la bandeja de entrada de ese tiempo, en caché | Para no avisar dos veces de lo mismo |
| Riesgos | `CalendarAdd`, `CalendarJoin` 🟢; `MailSend` 🔴; `calendar_delete` pregunta siempre; `mail_move` se deshace | Tabla en `docs/assistant/acciones.md` |

## Sin probar

No había credenciales ni Windows, así que todo esto está compilado pero **no se ha ejecutado nunca**:

- La interfaz WPF: Agenda, diálogos de citas, Opciones › Calendarios, Cuentas y Correo, y las plantillas de Eventos.
- OAuth real con Google y Microsoft (el flujo está probado con un servidor simulado: PKCE, redirección, tokens,
  renovación y revocación).
- Google Calendar y Microsoft Graph contra los servidores reales (probados con respuestas grabadas).
- IMAP/SMTP con MailKit, IDLE y XOAUTH2 (solo hay tests de `MailText`, `MailRules` y autoconfiguración).
- La pestaña «Agenda» de la web en un móvil.

Tests que deberían pasar en Windows: `dotnet test tests\KDesktop.Core.Tests` (incluye `LanguageTests`,
`PipeMessageTests`, `AssistantMapTests` y los nuevos `Calendar*Tests`, `AccountsTests`, `MailTextTests`,
`EventCalendarTriggerTests`) y `dotnet test tests\KDesktop.Assistant.Tests` (`BenchTests` comprueba que los casos
nuevos de calendario y correo piden herramientas que existen).

## Cómo probarlo

1. **Calendario propio**: Agenda › «Nueva cita» con una repetición; editar una aparición y elegir «esta y las
   siguientes»; exportar a `.ics` e importarlo en otro calendario.
2. **Suscripción**: Opciones › Calendarios › añadir la dirección `.ics` de festivos (por ejemplo la de Google).
3. **Google**: crear el Client ID con la guía de Opciones › Cuentas, conectar con «Calendario» y «Correo», elegir qué
   calendarios se ven y crear, mover y borrar una cita desde K-Desktop y desde Google.
4. **Outlook**: registrar la aplicación en entra.microsoft.com (guía en Opciones › Cuentas), conectar y repetir.
5. **Correo**: Opciones › Correo › añadir una cuenta con contraseña de aplicación (Gmail, iCloud, Yahoo) o con la
   cuenta conectada (Gmail, Outlook), «Probar», mandarse un correo y ver el aviso; un evento «Llega un correo» con
   un comando; la acción «Enviar correo».
6. **Asistente**: «¿qué tengo mañana?», «¿tengo correos nuevos?», «léeme el último» y luego pedir otra cosa: debe
   preguntar antes de usar una herramienta que cambie algo.

## Pendiente

- Número de no leídos en el icono de la bandeja y avisos de correo en Telegram.
- Correo en la web del móvil (la agenda sí está).
- Logros de calendario y correo.
- Registrar la aplicación de Microsoft de K-Desktop (ver arriba).
