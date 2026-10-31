import { t } from '../lib/i18n';
/** The actions of K-Desktop by family (src/KDesktop.Core/Model/ActionCatalog.cs, English names and descriptions). */
export type ActionInfo = { name: string; desc: string; icon: string; color: string };
export type Family = { id: string; name: string; icon: string; color: string; actions: ActionInfo[] };

export const FAMILIES: Family[] = [
  {
    id: 'apps', name: t('Open and run'), icon: 'open_24_filled', color: 'blue', actions: [
      { name: t('Run application'), desc: t('A program, file or shortcut, as user or administrator'), icon: 'open_24_filled', color: 'blue' },
      { name: t('Open folder'), desc: t('A folder or a special Windows location'), icon: 'folder_open_24_filled', color: 'yellow' },
      { name: t('Open web page'), desc: t('A URL in the browser of your choice'), icon: 'globe_24_filled', color: 'sky' },
      { name: t('Run script'), desc: t('CMD or PowerShell, written right here'), icon: 'window_console_20_filled', color: 'dark' },
    ],
  },
  {
    id: 'windows', name: t('Windows'), icon: 'window_new_24_filled', color: 'sky', actions: [
      { name: t('Control application'), desc: t('Activate, close or wait for an app'), icon: 'window_apps_24_filled', color: 'indigo' },
      { name: t('Window'), desc: t('Maximize, halves, quarters, thirds, monitors…'), icon: 'window_new_24_filled', color: 'sky' },
      { name: t('Always on top'), desc: t('Pin the active window above the rest'), icon: 'pin_24_filled', color: 'orange' },
    ],
  },
  {
    id: 'input', name: t('Keyboard and mouse'), icon: 'keyboard_24_filled', color: 'indigo', actions: [
      { name: t('Send keys / text'), desc: t('Combinations, text or mouse moves'), icon: 'keyboard_24_filled', color: 'indigo' },
      { name: t('Clipboard'), desc: t('Copy, transform and paste text'), icon: 'clipboard_text_edit_24_filled', color: 'brown' },
      { name: t('Play macro'), desc: t('Keyboard and mouse with their timing'), icon: 'record_24_filled', color: 'red' },
      { name: t('K-Board'), desc: t('Turn a remap profile on or off'), icon: 'keyboard_layout_float_24_filled', color: 'purple' },
      { name: t('Dictate'), desc: t('Types what you say, no AI'), icon: 'mic_sparkle_24_filled', color: 'teal' },
    ],
  },
  {
    id: 'sound', name: t('Sound'), icon: 'speaker_2_24_filled', color: 'teal', actions: [
      { name: t('Volume'), desc: t('Speakers, microphone or one app'), icon: 'speaker_2_24_filled', color: 'teal' },
      { name: t('Audio output'), desc: t('Switch the default device'), icon: 'headphones_sound_wave_24_filled', color: 'purple' },
      { name: t('Play sound'), desc: 'A bundled sound or a .wav', icon: 'music_note_2_24_filled', color: 'pink' },
      { name: t('Record audio'), desc: t('PC, microphone, or the last seconds'), icon: 'mic_pulse_24_filled', color: 'red' },
    ],
  },
  {
    id: 'screen', name: t('Screen'), icon: 'desktop_24_filled', color: 'green', actions: [
      { name: t('Screenshot'), desc: t('Region, window, monitor or all'), icon: 'screenshot_24_filled', color: 'green' },
      { name: t('Copy text from the screen'), desc: t('Windows text recognition'), icon: 'scan_text_24_filled', color: 'lime' },
      { name: t('Magnifier'), desc: t('A lens that follows the mouse'), icon: 'zoom_in_24_filled', color: 'gold' },
      { name: t('Screen'), desc: t('Off, projection, brightness, resolution'), icon: 'desktop_24_filled', color: 'blue' },
      { name: t('Windows theme'), desc: t('Light, dark or toggle'), icon: 'dark_theme_24_filled', color: 'dark' },
    ],
  },
  {
    id: 'network', name: t('Network and internet'), icon: 'globe_24_filled', color: 'pink', actions: [
      { name: t('Web request'), desc: t('GET, POST… Home Assistant, APIs'), icon: 'arrow_swap_24_filled', color: 'teal' },
      { name: t('Wake a computer'), desc: t('Wake-on-LAN, and wait for it'), icon: 'desktop_pulse_24_filled', color: 'green' },
      { name: t('Wi-Fi and Bluetooth'), desc: t('On, off or toggle'), icon: 'wifi_1_24_filled', color: 'sky' },
    ],
  },
  {
    id: 'system', name: t('System'), icon: 'power_24_filled', color: 'red', actions: [
      { name: t('Hidden files'), desc: t('Show or hide them in Explorer'), icon: 'eye_off_24_filled', color: 'gray' },
      { name: t('Power plan'), desc: t('High performance, Balanced…'), icon: 'battery_10_24_filled', color: 'lime' },
      { name: t('Power'), desc: t('Shut down, restart, sleep, lock'), icon: 'power_24_filled', color: 'red' },
    ],
  },
  {
    id: 'kdesktop', name: t('K-Desktop and flow'), icon: 'flash_24_filled', color: 'gold', actions: [
      { name: t('Show message'), desc: t('An on-screen message'), icon: 'chat_24_filled', color: 'blue' },
      { name: t('If…'), desc: t('Check first; stop or skip'), icon: 'branch_fork_24_filled', color: 'orange' },
      { name: t('Wait'), desc: t('Pause before the next action'), icon: 'timer_24_filled', color: 'purple' },
      { name: t('Commands'), desc: t('Run, enable or disable another'), icon: 'flash_24_filled', color: 'gold' },
      { name: t('Open QuickMenu'), desc: t('At the mouse position'), icon: 'text_bullet_list_tree_24_filled', color: 'indigo' },
    ],
  },
  {
    id: 'assistant', name: t('Assistants'), icon: 'bot_24_filled', color: 'purple', actions: [
      { name: t('Assistant: action'), desc: t('One of its own actions'), icon: 'bot_24_filled', color: 'purple' },
      { name: t('Assistant: ask'), desc: t('As if you said it out loud'), icon: 'chat_bubbles_question_24_filled', color: 'pink' },
      { name: t('Assistant: say'), desc: t('Says something, no AI'), icon: 'megaphone_24_filled', color: 'orange' },
      { name: t('Assistant: brain'), desc: t('Free or load the AI model'), icon: 'brain_circuit_24_filled', color: 'indigo' },
    ],
  },
];

/**
 * The actions as K-Desktop 5.0.451 lists them in «Add action» (ActionCatalog.cs: ten families; names from en.json), for
 * the short videos.
 */
const a = (name: string, icon: string, color: string): ActionInfo => ({ name: t(name), desc: '', icon, color });
export const FAMILIES_NOW: Family[] = [
  { id: 'apps', name: t('Open and run'), icon: 'open_24_filled', color: 'blue', actions: [
    a('Run application', 'open_24_filled', 'blue'), a('Open folder', 'folder_open_24_filled', 'yellow'),
    a('Open web page', 'globe_24_filled', 'sky'), a('Run script', 'window_console_20_filled', 'dark'),
  ] },
  { id: 'windows', name: t('Windows'), icon: 'window_new_24_filled', color: 'sky', actions: [
    a('Control application', 'window_apps_24_filled', 'indigo'), a('Window', 'window_new_24_filled', 'sky'),
    a('Window layout', 'grid_24_filled', 'teal'), a('Always on top (on/off)', 'pin_24_filled', 'orange'),
  ] },
  { id: 'input', name: t('Keyboard and mouse'), icon: 'keyboard_24_filled', color: 'indigo', actions: [
    a('Send keys / text', 'keyboard_24_filled', 'indigo'), a('Clipboard', 'clipboard_text_edit_24_filled', 'brown'),
    a('Play macro', 'record_24_filled', 'red'), a('K-Board', 'keyboard_layout_float_24_filled', 'purple'), a('Dictate', 'mic_sparkle_24_filled', 'teal'),
  ] },
  { id: 'sound', name: t('Sound'), icon: 'speaker_2_24_filled', color: 'teal', actions: [
    a('Volume', 'speaker_2_24_filled', 'teal'), a('Audio output', 'headphones_sound_wave_24_filled', 'purple'),
    a('Audio input', 'mic_24_filled', 'sky'), a('Media', 'play_circle_24_filled', 'pink'),
    a('Play sound', 'music_note_2_24_filled', 'orange'), a('Record audio', 'mic_pulse_24_filled', 'red'),
  ] },
  { id: 'screen', name: t('Screen'), icon: 'desktop_24_filled', color: 'green', actions: [
    a('Screenshot', 'screenshot_24_filled', 'green'), a('Record video', 'video_clip_24_filled', 'red'),
    a('Copy text from the screen', 'scan_text_24_filled', 'lime'), a('Magnifier', 'zoom_in_24_filled', 'gold'),
    a('Ruler', 'ruler_24_filled', 'orange'), a('Colour picker', 'color_24_filled', 'pink'),
    a('Screen', 'desktop_24_filled', 'blue'), a('Windows theme', 'dark_theme_24_filled', 'dark'),
  ] },
  { id: 'network', name: t('Network and internet'), icon: 'globe_24_filled', color: 'pink', actions: [
    a('Web request', 'arrow_swap_24_filled', 'teal'), a('Wake a computer', 'desktop_pulse_24_filled', 'green'),
    a('Wi-Fi and Bluetooth', 'wifi_1_24_filled', 'sky'), a('Send to phone', 'phone_24_filled', 'sky'), a('Send mail', 'mail_24_filled', 'blue'),
    a('Send via Telegram', 'send_24_filled', 'sky'),
  ] },
  { id: 'calendar', name: t('Calendar'), icon: 'calendar_ltr_24_filled', color: 'orange', actions: [
    a('Create event', 'calendar_add_24_filled', 'orange'), a('Join the meeting', 'video_person_24_filled', 'purple'),
  ] },
  { id: 'system', name: t('System'), icon: 'power_24_filled', color: 'red', actions: [
    a('Files and folders', 'document_copy_24_filled', 'yellow'), a('Hidden files (on/off)', 'eye_off_24_filled', 'gray'),
    a('Keep awake', 'drink_coffee_24_filled', 'brown'), a('Power plan', 'battery_10_24_filled', 'lime'), a('Power', 'power_24_filled', 'red'),
  ] },
  { id: 'flow', name: t('Flow'), icon: 'flash_24_filled', color: 'gold', actions: [
    a('Show message', 'chat_24_filled', 'blue'), a('Show notification', 'alert_24_filled', 'orange'), a('If…', 'branch_fork_24_filled', 'orange'),
    a('Wait', 'timer_24_filled', 'purple'), a('Commands', 'flash_24_filled', 'gold'), a('Open QuickMenu', 'text_bullet_list_tree_24_filled', 'indigo'),
  ] },
  { id: 'assistants', name: t('Assistants'), icon: 'bot_24_filled', color: 'purple', actions: [
    a('Assistant: action', 'bot_24_filled', 'purple'), a('Assistant: ask', 'chat_bubbles_question_24_filled', 'pink'),
    a('Assistant: chat', 'chat_multiple_24_filled', 'blue'), a('Assistant: say', 'megaphone_24_filled', 'orange'),
    a('Assistant: use a tool', 'wrench_24_filled', 'teal'), a('Assistant: brain', 'brain_circuit_24_filled', 'indigo'),
  ] },
];
