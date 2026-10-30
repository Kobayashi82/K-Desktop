# Telegram

K-Desktop can have its own identity in Telegram: you write to it from your phone to run commands or talk to the
assistant, it tells you things (important mails, what your commands send), and you decide who else may write to it and
what each person may do. It works without the Assistant module; with it, Telegram is one more way of talking to the
assistants.

There are two ways, and you can have both at once. Each one has its own page in *Settings → Accounts*, with its own owner,
contacts and options:

| | **Telegram Bot** | **Telegram** (an account with a number) |
|---|---|---|
| What it is | A bot you create in two minutes with @BotFather | A normal Telegram account, best with a phone just for K-Desktop |
| Writes first | Only to people who wrote to it before | To anybody in its contacts |
| Buttons | Yes (commands, Yes / No) | No: the options come numbered and you answer with the number or the word |
| Files | Up to 50 MB out, 20 MB in | Up to 2 GB |

## The bot

1. **How to create the bot** opens a step-by-step guide: in Telegram, open @BotFather, send `/newbot`, give it a name and a
   username ending in «bot», and copy the **token** it gives you.
2. *Settings → Accounts → Telegram Bot → Add a bot*, paste the token and **Connect**. It is checked with Telegram before
   keeping it, encrypted.
3. **Link my account**: send the bot the 6-digit code it shows, or open its link, or scan the QR code with your phone. From
   then on, you are its **owner**.

## The account with a number

1. Have the account on a phone (better a real SIM than a virtual number).
2. **How to get the api_id and api_hash** opens the guide: Telegram asks for your own free «application» at
   my.telegram.org (*API development tools*); you copy its two values.
3. *Settings → Accounts → Telegram → Log in*: the api_id, the api_hash and the number; then the code Telegram sends to the
   app (or by SMS) and, if the account has two-step verification, its password. The session is kept encrypted, only for
   your user on this PC. **Log out** closes it in Telegram too.
4. **Link my account**: from your own Telegram, send the account the code it shows (find it by its number or username).

Telegram watches accounts that write to many people. K-Desktop goes slowly on purpose (a message every little more than a
second, and at most 3 people who never wrote to it an hour by default), never writes in bulk, and says on the page if
Telegram limits the account for a while.

## Who can write to it

The owner can do everything. Anyone else does what their **level** allows, also in groups; the level of whoever writes is
what counts, not the chat's:

| Level | What they can do |
|---|---|
| **Ignore** | Nothing: their messages are dropped |
| **Chat only** | Talk with the assistant, without tools |
| **Chosen commands** | Run the commands you choose, without AI |
| **Tools that only read** | Chat and tools that change nothing (the weather, the time, searching…) |
| **Chosen tools** | Chat and the tools and commands you choose |
| **Like you** | The same as you on the PC |

Whatever needs confirming is always asked **to you**, never to whoever asked: on the PC if you are there, otherwise by
Telegram with *Yes* / *No*, which expire after 5 minutes without doing anything.

- **Contacts**: each one with their level, **K-Desktop may write to them**, the commands and tools chosen, **Who answers
  them** and **How it answers them**. In the account, **Add contact** brings someone from its Telegram contacts or adds them
  **By number** (also to its contacts in Telegram, so it can write to them first).
- **Someone new** who writes waits for your decision: you are told on the PC and on Telegram, with **Allow**, **Ignore**
  and **Block**.
- **Everyone else**: the level of anybody who is not a contact. Above *Chat only* it needs **I understand the risk**:
  anyone who finds it could use your PC.
- **Limits**: messages a minute from each person, **Only me while I'm gaming** (with something full screen it only answers
  you) and how many messages an hour to people who never wrote.
- **Groups**: it only answers when mentioned or when someone replies to its message; each group can be ignored.
- **Blocked**: their messages are dropped silently and K-Desktop never writes to them.

## Commands from Telegram

`/commands` shows the commands that person may run (as buttons in the bot, numbered in the account); writing `/` and the
name runs one too (`/game_mode` runs «Game mode»: case, accents and spaces don't matter). It answers how it went («✓ Game
mode»). Photos, voice notes and files that you and your contacts send are saved in *Downloads*.

## Talking to the assistant

What is written without `/` goes to the assistant (with the Assistant module on), with each person's limits:

- **Who answers**: the one on screen or one in particular (and each contact can have their own).
- **How it answers**: *As they write* (a voice note is answered with voice, a text with text), *With text*, *With its
  voice* (a voice note in the assistant's own voice) or *With text and voice*.
- **Voice notes** are turned into text on your PC (with the voice models of *Settings → Voice*); **Say what it understood**
  answers first with that text. A **photo** without text goes to the assistant to look at it.
- **Say what it did** puts under the answer what it did («✓ Opened Spotify»).

Texts from others are data, not orders: in a group, with a forwarded message or a file from someone else, anything that
acts asks you first. Whoever is not the owner has a conversation of their own and does not see the assistant's memory.

## Which one writes

- It **answers by where it was written to**: to the bot, the bot; to the account, the account.
- To **write first**, *Automatic* uses the account when that person is its owner or one of its contacts, and otherwise the
  bot. The **Send via Telegram** action has **With** (*Automatic*, *The bot*, *The account*), and you can tell the
  assistant «by the bot» or «by the account».
- **Important mails**: in *Telegram notices* of each page; if both have it on, it comes once, by the account.

## Actions, events and tools

| | |
|---|---|
| **Send via Telegram** (action) | To you or a contact: a text with variables, a file, a screenshot or what is on the clipboard. Who got it goes into `{result}` |
| **A Telegram message arrives** (event) | Only your messages or someone's, with some words, of a kind (text, voice note, photo or file). The commands get `{telegram_text}`, `{telegram_from}`, `{telegram_file}` and `{telegram_id}` |
| Assistant tools | **Send a Telegram message**, **Send a file by Telegram** (a file, a screenshot or the last one that arrived) and **Read Telegram** (the last messages of a chat). Sending to you asks until you say «don't ask again»; to anyone else, always |

*When I send «note …» from my phone, add it to my notes.*
