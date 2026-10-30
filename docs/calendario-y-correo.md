# Calendario y correo: notas de la implementación

Notas de las versiones 5.0.415 a 5.0.422 (planes `calendario-plan.md` y `correo-plan.md`): qué se decidió donde el
plan dejaba la pregunta abierta, qué no se ha podido probar y cómo configurarlo.

## Decisiones

| Pregunta | Decisión | Por qué |
|---|---|---|
| ¿Biblioteca de iCalendar o propia? | Propia (`Calendar/Recurrence.cs`, `IcsFormat.cs`) | RRULE con BYDAY ordinales, BYMONTHDAY negativo, BYSETPOS, COUNT/UNTIL, EXDATE y RECURRENCE-ID caben en poco código, sin dependencias y con tests; las zonas se resuelven con `TimeZoneInfo` (IANA y Windows) |
| ¿Dónde se guarda? | JSON en `%APPDATA%\K-Desktop 5\Calendar` (propio, cachés de suscripciones y de cuentas, cola sin conexión) | Igual que el resto de la configuración; sin SQLite |
| Cliente de Google | Cada usuario pone su Client ID y secreto (guía en Opciones › Cuentas) | Verificar una app con permisos de Calendar y Gmail exige una auditoría; sin verificar, en «En producción» vale para uno mismo |
| Microsoft (Outlook, Hotmail) | **Quitado en la 5.0.429**: sin cuentas de Microsoft, sin calendario de Outlook por Graph y sin correo de Outlook | Para conectar hace falta registrar una aplicación en Entra ID, y una cuenta personal no tiene directorio: Entra la rechaza (AADSTS16000) y crear uno exige una cuenta de Azure con tarjeta. El calendario de Outlook se sigue viendo en solo lectura con su dirección `.ics`; el correo de Outlook y Hotmail no (Microsoft ya no admite contraseña en IMAP), y K-Desktop lo dice al añadirlo. El código está en el historial de git (5.0.419–5.0.428) |
| Tokens | El de refresco cifrado con DPAPI; el de acceso solo en memoria; nunca salen al proceso de los asistentes | Las herramientas piden al calendario y al correo de K-Desktop por la tubería |
| Avisos de citas de Google | Apagados por defecto (`AccountReminders`) | Google ya avisa en el móvil; se encienden en Opciones › Calendario |
| ¿Puede enviar el asistente? | Sí (`AssistantMaySend`); `mail_send` pregunta siempre salvo que se elija «Sin preguntar» en Asistente › Herramientas (5.0.424) | Es la petición más habitual («contesta a Ana que…»); tras leer un correo pregunta igualmente |
| Contenido de los correos | `mail_list` y `mail_read` marcan el turno como «leído de fuera»: desde ahí, cualquier herramienta que no sea de solo lectura pregunta (`tools.askAfterMail`) y el texto llega envuelto como no fiable | Defensa contra inyección: lo que diga un correo no es una orden |
| Marcar como leído | No (`MarkReadByAssistant` apagado) | Que el asistente lea no debe cambiar el estado del buzón |
| Avisos de correo nuevo | «Importantes» por defecto: los de los remitentes o con las palabras en el asunto de Opciones › Correo (se puede elegir «Todos» o «Ninguno» por cuenta) | Avisar de todo es ruido en cuanto hay publicidad |
| Días que se guardan | 30 (`KeepDays`): las cabeceras de la bandeja de entrada de ese tiempo, en caché | Para no avisar dos veces de lo mismo |
| Riesgos | `CalendarAdd`, `CalendarJoin` 🟢; `MailSend` 🔴; `calendar_delete` pregunta siempre; `mail_move` se deshace | Tabla en `docs/assistant/acciones.md` |

## Probado

En Windows, con cuentas reales (30/10/2026): la interfaz (Agenda, diálogo de citas, Opciones › Cuentas, Correo y
Calendario), conectar una cuenta de Google con su propio Client ID (la guía de Opciones › Cuentas), ver y crear citas de
Google Calendar en los dos sentidos, y una cuenta de Gmail con contraseña de aplicación: vigilancia con IDLE y aviso de
un correo nuevo importante. Sin probar con servidores reales: Gmail por OAuth (XOAUTH2) y la pestaña «Agenda» del móvil.

## Pendiente

- Número de no leídos en el icono de la bandeja y avisos de correo en Telegram.
- Correo en la web del móvil (la agenda sí está).
- Logros de calendario y correo.
