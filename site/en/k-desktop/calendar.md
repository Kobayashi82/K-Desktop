# Calendar and agenda

K-Desktop keeps your appointments in one place: calendars of its own, your Google calendars and any `.ics` address
(Outlook, holidays, sports…). It reminds you before each event, fires [events](events.md), has actions to create
events and join meetings, and the assistants can check and change your agenda.

## The Agenda

**Agenda**, in the side menu, lists the coming days with their events, each in its calendar's colour. From there:

- **New event**, or a click on one to **Edit** it (or **View** it, if its calendar is read-only); right click: *Join*,
  *Copy* and *Delete*.
- **Join** opens the Teams, Meet or Zoom link of an online meeting.
- **Search events…**, **Show earlier days** and **Show more days**.
- **Update now** reads the server calendars again; **Import .ics** and **Export to .ics** move events between calendars
  and other programs.

An event has a title, calendar, start and end (or *All day*), **Repeats** (daily, every weekday, weekly, monthly on a day
or on «the second Tuesday», yearly, every so many of them; for ever, until a day or a number of times), **Reminder**,
location, **Meeting link**
(found by itself in the location or the notes) and notes. Changing or deleting a repeating event asks: *This one only*,
*This and following* or *All of them*. An event from a read-only calendar can only be viewed.

## Calendars

*Settings → Calendar* lists every calendar. Each one has its **name** (the assistant knows them by it: «put it in Work»),
**colour**, **Show in the agenda** and a **Reminder for new events**. **Default calendar** is where new events go when you
do not say another one.

- **New calendar**: your own (Work, Family…), kept on this PC and in its backups.
- **Subscribe to an .ics**: a read-only calendar from an address, read every so often (15 minutes by default). It is the
  way to see Outlook, or Google without connecting the account (*Where to find the .ics address* explains how). The
  address is kept encrypted: a secret calendar address lets anybody who has it read the calendar.
- **Google**: connect the account in *Settings → Accounts → Google* (calendar only, or read and edit), then **Show its
  calendars** and choose which ones. Google asks for your own free application (a *Client ID*); *How to get the Client ID*
  walks you through it. K-Desktop never sees your password: you sign in in the browser.

## Reminders

*Settings → Calendar → Reminders* chooses **How it reminds you**: an **on-screen message (OSD)**, a **notification**
(with *Join* when the event has a meeting link) or **an assistant says it** (if it is on the desktop; otherwise an OSD).
**Google reminders here too** is off by default, because the phone already gives them. **Try a reminder** shows one.

## Actions and events

| | |
|---|---|
| **Create event** (action) | Adds an event at a time or in a while, with title, location and notes that can have variables |
| **Join the meeting** (action) | Opens the link of the current meeting (from 10 minutes before it starts) or the next one |
| **Calendar event** (event) | Some minutes before an event, when it starts or when it ends: from a calendar, with a word in the title, online meetings only, with a location… The commands get `{appointment}`, `{appointment_time}`, `{appointment_place}`, `{appointment_notes}`, `{appointment_link}` and `{appointment_calendar}` |

*10 minutes before a Work meeting: do not disturb and open Teams.*

## The assistants

With the [Assistant module](../assistant/index.md), the tools **Check the calendar**, **Add an event**, **Change an
event** and **Delete an event**: «what do I have tomorrow?», «put the dentist on Friday at 5», «move the meeting to 11»,
«cancel Thursday's dinner». Adding never asks, changing asks until you say «don't ask again», and deleting always asks;
all three can be undone.
