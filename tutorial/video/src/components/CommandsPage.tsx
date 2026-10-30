import React from 'react';
import { C, SANS } from '../theme';
import { ActionRow } from './CommandCard';
import { Fluent, KdIcon } from './Icon';
import { HotkeyChip } from './Keys';
import { Card, Check, GhostButton, GoldButton, Toggle } from './Ui';
import { t } from '../lib/i18n';

export type ListItem = { name: string; sub: string; hotkey?: string; icon: string; color: string };

export const COMMAND_LIST: ListItem[] = [
  { name: t('Browser'), sub: t('Open web page'), hotkey: 'CTRL + ALT + B', icon: 'globe_24_filled', color: 'blue' },
  { name: t('Downloads'), sub: t('Downloads'), hotkey: 'CTRL + ALT + D', icon: 'arrow_download_24_filled', color: 'teal' },
  { name: t('Magnifier'), sub: t('Magnifier: on / off'), hotkey: 'CTRL + Wheel Up', icon: 'zoom_in_24_filled', color: 'gold' },
  { name: t('Main menu'), sub: t('Open QuickMenu Main'), hotkey: 'Mouse 5', icon: 'text_bullet_list_tree_24_filled', color: 'indigo' },
  { name: t('Music'), sub: t('AIMP.exe'), hotkey: 'CTRL + ALT + M', icon: 'music_note_2_24_filled', color: 'pink' },
  { name: t('Notepad'), sub: t('notepad.exe'), hotkey: 'CTRL + ALT + N', icon: 'document_text_24_filled', color: 'sky' },
  { name: t('Recycle Bin'), sub: t('Recycle Bin'), icon: 'delete_24_filled', color: 'gray' },
  { name: t('Region capture'), sub: t('A region · copy · save'), hotkey: 'CTRL + SHIFT + S', icon: 'screenshot_24_filled', color: 'green' },
  { name: t('Sound settings'), sub: t('Sound devices'), icon: 'sound_source_24_filled', color: 'teal' },
];

export type EditorState = {
  name: string;
  /** The command's icon (the gold flash of a new one by default). */
  icon?: string;
  color?: string;
  caret?: boolean;
  hotkeys: { keys: string[]; fresh?: number }[];
  capturing?: boolean;
  actions: ActionRow[];
  shown: number[];
  hot?: number;
  addAction?: number;
};

const ListRow: React.FC<{ item: ListItem; selected?: boolean; show?: number }> = ({ item, selected, show = 1 }) => (
  <div style={{
    position: 'relative', display: 'flex', alignItems: 'center', gap: 14, padding: '10px 12px', borderRadius: 10, marginBottom: 4,
    background: selected ? 'rgba(255,255,255,0.08)' : 'transparent', opacity: show, transform: `translateX(${(1 - show) * -30}px)`,
    height: 70 * Math.min(1, show * 1.3), boxSizing: 'border-box', overflow: 'hidden',
  }}>
    {selected && <div style={{ position: 'absolute', left: 0, top: 18, bottom: 18, width: 3, borderRadius: 2, background: C.accent }} />}
    <KdIcon icon={item.icon} color={item.color} size={40} />
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 }}>
      <div style={{ fontFamily: SANS, fontWeight: 600, fontSize: 16, color: C.text, whiteSpace: 'nowrap' }}>{item.name}</div>
      <div style={{ fontFamily: SANS, fontSize: 13, color: C.dim, whiteSpace: 'nowrap' }}>{item.sub}</div>
      {item.hotkey && <div style={{ fontFamily: SANS, fontSize: 13, fontWeight: 600, color: C.accentText }}>{item.hotkey}</div>}
    </div>
  </div>
);

/** The Commands page: the list on the left, the editor of the selected command on the right. */
export const CommandsPage: React.FC<{ list: ListItem[]; fresh?: ListItem & { show: number }; editor?: EditorState; newPress?: number; scrollY?: number; options?: React.ReactNode }> = ({ list, fresh, editor, newPress = 0, scrollY = 0, options }) => (
  <div style={{ position: 'absolute', inset: 0, fontFamily: SANS, color: C.text }}>
    {/* List */}
    <div style={{ position: 'absolute', left: 24, top: 22, width: 330, bottom: 0 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 18 }}>
        <span style={{ fontSize: 30, fontWeight: 700 }}>{t('Commands')}</span>
        <span style={{ fontSize: 13, fontWeight: 700, color: C.accentText, background: C.accentSoft, padding: '3px 9px', borderRadius: 9 }}>{list.length + (fresh ? 1 : 0)}</span>
      </div>
      <div style={{ height: 42, borderRadius: 8, background: C.card2, display: 'flex', alignItems: 'center', gap: 10, padding: '0 14px', fontSize: 15, color: C.faint, marginBottom: 10 }}>
        <Fluent name="search_24_regular" size={18} color={C.dim} /> {t('Search by name, hotkey or action…')}
      </div>
      <div style={{ height: 42, borderRadius: 8, background: C.card2, display: 'flex', alignItems: 'center', padding: '0 14px', fontSize: 15, marginBottom: 10, justifyContent: 'space-between' }}>
        {t('All commands')}{' '}<Fluent name="chevron_down_24_regular" size={16} color={C.dim} />
      </div>
      <div style={{ display: 'flex', gap: 8, marginBottom: 14 }}>
        <GoldButton icon="add_24_regular" press={newPress} style={{ flex: 1 }}>{t('New command')}</GoldButton>
        <GhostButton icon="eye_off_24_regular" />
      </div>
      {fresh && fresh.show > 0 && <ListRow item={fresh} selected show={fresh.show} />}
      {list.map((it, i) => <ListRow key={i} item={it} selected={!fresh && i === 4} />)}
    </div>
    {/* Editor */}
    <div style={{ position: 'absolute', left: 378, top: 22, right: 24, bottom: 0, overflow: 'hidden' }}>
      {!editor ? (
        <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 10, color: C.dim }}>
          <Fluent name="flash_24_regular" size={56} color={C.faint} />
          <div style={{ fontSize: 22, fontWeight: 600, color: C.text }}>{t('No command selected')}</div>
          <div style={{ fontSize: 15 }}>{t('Pick a command from the list or create a new one.')}</div>
        </div>
      ) : (
        <div style={{ transform: `translateY(${-scrollY}px)`, display: 'flex', flexDirection: 'column', gap: 16 }}>
          <Card style={{ display: 'flex', alignItems: 'center', gap: 16 }} pad={18}>
            <KdIcon icon={editor.icon ?? 'flash_24_filled'} color={editor.color ?? 'gold'} size={64} />
            <div style={{ width: 260, height: 46, borderRadius: 8, background: C.card2, border: `1px solid ${editor.caret ? C.accentText : C.line}`, display: 'flex', alignItems: 'center', padding: '0 14px', fontSize: 20, fontWeight: 600 }}>
              {editor.name}{editor.caret && <span style={{ width: 2, height: 24, background: C.accentText, marginLeft: 2 }} />}
            </div>
            <GhostButton icon="eye_24_regular" />
            <GhostButton icon="plug_connected_24_regular" />
            <div style={{ flex: 1 }} />
            <Toggle on={1} /> <span style={{ fontSize: 15 }}>{t('Enabled')}</span>
            <GoldButton icon="play_24_regular">{t('Run')}</GoldButton>
            <GhostButton icon="copy_24_regular" />
            <GhostButton icon="delete_24_regular" />
          </Card>
          <Card pad={20}>
            <div style={{ fontSize: 17, fontWeight: 600, marginBottom: 14 }}>{t('Hotkeys')}</div>
            <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
              {editor.hotkeys.map((h, i) => <HotkeyChip key={i} keys={h.keys} fresh={h.fresh} />)}
              <div style={{
                display: 'flex', alignItems: 'center', gap: 8, padding: '9px 14px', borderRadius: 10, fontSize: 15, fontWeight: 600,
                background: editor.capturing ? C.accentSoft : C.card2, color: editor.capturing ? C.accentText : C.text, border: `1px solid ${editor.capturing ? C.accentText : C.line}`,
              }}>
                <Fluent name="add_24_regular" size={16} color={editor.capturing ? C.accentText : C.text} />
                {editor.capturing ? t('Press a combination… (Esc cancels)') : t('Add hotkey')}
              </div>
            </div>
            <div style={{ fontSize: 13, color: C.dim, marginTop: 14 }}>{t('Keys, mouse buttons or combinations of both.')}</div>
          </Card>
          <Card pad={20}>
            <div style={{ display: 'flex', alignItems: 'center', marginBottom: 14 }}>
              <span style={{ fontSize: 17, fontWeight: 600 }}>{t('Actions')}</span>
              <div style={{ flex: 1 }} />
              <GoldButton icon="add_24_regular" press={editor.addAction ?? 0}>{t('Add action')}</GoldButton>
            </div>
            {editor.actions.length === 0 || editor.shown.every(s => s <= 0) ? (
              <div style={{ fontSize: 15, color: C.dim, padding: '18px 0' }}>{t('This command does nothing yet. Add its first action.')}</div>
            ) : null}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {editor.actions.map((a, i) => {
                const k = editor.shown[i] ?? 0;
                if (k <= 0) return null;
                const hot = editor.hot === i;
                return (
                  <div key={i} style={{
                    display: 'flex', alignItems: 'center', gap: 16, padding: '0 18px', height: 64 * Math.min(1, k * 1.2), overflow: 'hidden', borderRadius: 10,
                    background: hot ? 'rgba(242,193,74,0.14)' : C.row, border: `1px solid ${hot ? 'rgba(242,193,74,0.6)' : 'transparent'}`,
                    opacity: Math.min(1, k * 1.4), transform: `translateY(${(1 - k) * 16}px)`,
                  }}>
                    <div style={{ width: 44, height: 44, borderRadius: 10, background: 'rgba(242,193,74,0.12)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                      <KdIcon icon={a.icon} color={a.color} size={30} />
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                      <div style={{ fontSize: 16, fontWeight: 600 }}>{a.name}</div>
                      <div style={{ fontSize: 13, color: C.dim }}>{a.detail}</div>
                    </div>
                    <div style={{ flex: 1 }} />
                    <Check />
                    {['play_24_regular', 'copy_24_regular', 'arrow_up_24_regular', 'arrow_down_24_regular'].map(ic => <Fluent key={ic} name={ic} size={18} color={C.dim} />)}
                    <Fluent name="delete_24_regular" size={18} color={C.danger} />
                  </div>
                );
              })}
            </div>
          </Card>
          {options}
        </div>
      )}
    </div>
  </div>
);
