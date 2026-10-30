# Mail

K-Desktop watches your mail accounts and tells you what matters, fires [events](events.md) when a mail arrives, sends
mails from your commands, and lets the assistants search, sum up and draft. It is not a mail client: you keep reading
your mail where you always do.

## Accounts

*Settings → Mail → Add account*: the address, a name for the account (*Personal*, *Work*… — how the assistant knows it)
and how it signs in:

- **With a password (app password)**. Gmail, iCloud and Yahoo need an *app password* (*How to get an app password* explains
  where); other providers, your password. **Detect the servers** finds IMAP and SMTP by itself; if not, type them.
- **With the connected account**: Gmail without an app password, after connecting the account in *Settings → Accounts →
  Google* with *Gmail* ticked.

Outlook and Hotmail cannot be connected: Microsoft no longer lets programs sign in with a password. **Test** checks that it
can sign in to IMAP and SMTP and, if not, says why. The password is kept encrypted, only for your user on this PC.

Each account also has:

- **What for**: *Everything* (notices, events, assistant and sending) or *Send only*, an account just for K-Desktop's
  reports and notices, which only reaches the **Allowed addresses** (empty: its own address).
- **Notify about**: *Only important ones*, *Every new mail* or *Nothing* (only events and the assistant).
- **Mark the mails the assistant reads as read**.

## Important mail and notices

**Important mail** says which mails matter: **Senders** (addresses, domains like `@bank.com`, or names) and **Words in the
subject** (*invoice, urgent*). They are the ones told with *Only important ones* and the ones the assistant highlights.

**Notices** chooses how a new mail is told: a **notification** (a click opens the mail on the web: Gmail, iCloud, Yahoo…),
an **on-screen message (OSD)** or **an assistant says it** (if it is on the desktop; otherwise an OSD). **Try a reminder**
shows one. A mail with a calendar invitation always comes as a notification: clicking it adds the event to the
[calendar](calendar.md). The important ones can also reach you on [Telegram](telegram.md).

## Actions and events

| | |
|---|---|
| **Send mail** (action) | From one of your accounts, with text with variables and attached files (a screenshot, a report…), or **Leave it as a draft** |
| **Mail arrives** (event) | When a mail arrives at an account: from someone, with words in the subject, only with attachments or only the important ones. It can **save its attachments** in a folder (only some kinds: `*.pdf`). The commands get `{mail_subject}`, `{mail_from}`, `{mail_snippet}`, `{mail_attachments}`, `{mail_paths}` (the saved files) and `{mail_account}` |

*When the electricity bill arrives, save its PDF in the Bills folder and let me know.*

## The assistants

With the [Assistant module](../assistant/index.md): **Check mail** and **Read a mail** («anything from the bank?», «sum up
Ana's last mail»), **Leave a draft**, **Send a mail**, **Tidy up a mail** (mark it as read or unread, archive it or move it to the trash, never deleted for good) and **Add an
invitation to the calendar**. Reading never asks; sending always asks; the rest asks until you say «don't ask again» and
can be undone. A mail never gives orders: after reading one, the assistant asks before doing anything.

In *The assistant and mail*: **Assistants can send** (off, they only leave drafts) and **Days of mail kept** (only the
headers and a snippet, to search without asking the server).
