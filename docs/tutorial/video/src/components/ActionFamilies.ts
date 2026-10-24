/** The actions of K-Desktop by family (src/KDesktop.Core/Model/ActionCatalog.cs, English names and descriptions). */
export type ActionInfo = { name: string; desc: string; icon: string; color: string };
export type Family = { id: string; name: string; icon: string; color: string; actions: ActionInfo[] };

export const FAMILIES: Family[] = [
  {
    id: 'apps', name: 'Open and run', icon: 'open_24_filled', color: 'blue', actions: [
      { name: 'Run application', desc: 'A program, file or shortcut, as user or administrator', icon: 'open_24_filled', color: 'blue' },
      { name: 'Open folder', desc: 'A folder or a special Windows location', icon: 'folder_open_24_filled', color: 'yellow' },
      { name: 'Open web page', desc: 'A URL in the browser of your choice', icon: 'globe_24_filled', color: 'sky' },
      { name: 'Run script', desc: 'CMD or PowerShell, written right here', icon: 'window_console_20_filled', color: 'dark' },
    ],
  },
  {
    id: 'windows', name: 'Windows', icon: 'window_new_24_filled', color: 'sky', actions: [
      { name: 'Control application', desc: 'Activate, close or wait for an app', icon: 'window_apps_24_filled', color: 'indigo' },
      { name: 'Window', desc: 'Maximize, halves, quarters, thirds, monitors…', icon: 'window_new_24_filled', color: 'sky' },
      { name: 'Always on top', desc: 'Pin the active window above the rest', icon: 'pin_24_filled', color: 'orange' },
    ],
  },
  {
    id: 'input', name: 'Keyboard and mouse', icon: 'keyboard_24_filled', color: 'indigo', actions: [
      { name: 'Send keys / text', desc: 'Combinations, text or mouse moves', icon: 'keyboard_24_filled', color: 'indigo' },
      { name: 'Clipboard', desc: 'Copy, transform and paste text', icon: 'clipboard_text_edit_24_filled', color: 'brown' },
      { name: 'Play macro', desc: 'Keyboard and mouse with their timing', icon: 'record_24_filled', color: 'red' },
      { name: 'K-Board', desc: 'Turn a remap profile on or off', icon: 'keyboard_layout_float_24_filled', color: 'purple' },
      { name: 'Dictate', desc: 'Types what you say, no AI', icon: 'mic_sparkle_24_filled', color: 'teal' },
    ],
  },
  {
    id: 'sound', name: 'Sound', icon: 'speaker_2_24_filled', color: 'teal', actions: [
      { name: 'Volume', desc: 'Speakers, microphone or one app', icon: 'speaker_2_24_filled', color: 'teal' },
      { name: 'Audio output', desc: 'Switch the default device', icon: 'headphones_sound_wave_24_filled', color: 'purple' },
      { name: 'Play sound', desc: 'A bundled sound or a .wav', icon: 'music_note_2_24_filled', color: 'pink' },
      { name: 'Record audio', desc: 'PC, microphone, or the last seconds', icon: 'mic_pulse_24_filled', color: 'red' },
    ],
  },
  {
    id: 'screen', name: 'Screen', icon: 'desktop_24_filled', color: 'green', actions: [
      { name: 'Screenshot', desc: 'Region, window, monitor or all', icon: 'screenshot_24_filled', color: 'green' },
      { name: 'Copy text from the screen', desc: 'Windows text recognition', icon: 'scan_text_24_filled', color: 'lime' },
      { name: 'Magnifier', desc: 'A lens that follows the mouse', icon: 'zoom_in_24_filled', color: 'gold' },
      { name: 'Screen', desc: 'Off, projection, brightness, resolution', icon: 'desktop_24_filled', color: 'blue' },
      { name: 'Windows theme', desc: 'Light, dark or toggle', icon: 'dark_theme_24_filled', color: 'dark' },
    ],
  },
  {
    id: 'network', name: 'Network and internet', icon: 'globe_24_filled', color: 'pink', actions: [
      { name: 'Web request', desc: 'GET, POST… Home Assistant, APIs', icon: 'arrow_swap_24_filled', color: 'teal' },
      { name: 'Wake a computer', desc: 'Wake-on-LAN, and wait for it', icon: 'desktop_pulse_24_filled', color: 'green' },
      { name: 'Wi-Fi and Bluetooth', desc: 'On, off or toggle', icon: 'wifi_1_24_filled', color: 'sky' },
    ],
  },
  {
    id: 'system', name: 'System', icon: 'power_24_filled', color: 'red', actions: [
      { name: 'Hidden files', desc: 'Show or hide them in Explorer', icon: 'eye_off_24_filled', color: 'gray' },
      { name: 'Power plan', desc: 'High performance, Balanced…', icon: 'battery_10_24_filled', color: 'lime' },
      { name: 'Power', desc: 'Shut down, restart, sleep, lock', icon: 'power_24_filled', color: 'red' },
    ],
  },
  {
    id: 'kdesktop', name: 'K-Desktop and flow', icon: 'flash_24_filled', color: 'gold', actions: [
      { name: 'Show message', desc: 'An on-screen message', icon: 'chat_24_filled', color: 'blue' },
      { name: 'If…', desc: 'Check first; stop or skip', icon: 'branch_fork_24_filled', color: 'orange' },
      { name: 'Wait', desc: 'Pause before the next action', icon: 'timer_24_filled', color: 'purple' },
      { name: 'Commands', desc: 'Run, enable or disable another', icon: 'flash_24_filled', color: 'gold' },
      { name: 'Open QuickMenu', desc: 'At the mouse position', icon: 'text_bullet_list_tree_24_filled', color: 'indigo' },
    ],
  },
  {
    id: 'assistant', name: 'Assistants', icon: 'bot_24_filled', color: 'purple', actions: [
      { name: 'Assistant: action', desc: 'One of its own actions', icon: 'bot_24_filled', color: 'purple' },
      { name: 'Assistant: ask', desc: 'As if you said it out loud', icon: 'chat_bubbles_question_24_filled', color: 'pink' },
      { name: 'Assistant: say', desc: 'Says something, no AI', icon: 'megaphone_24_filled', color: 'orange' },
      { name: 'Assistant: brain', desc: 'Free or load the AI model', icon: 'brain_circuit_24_filled', color: 'indigo' },
    ],
  },
];
