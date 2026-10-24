import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../components/Capture';
import { Part } from '../components/Chapter';
import { Fluent, KdIcon } from '../components/Icon';
import { Keycap } from '../components/Keys';
import { Sfx } from '../components/Sfx';
import { Toggle } from '../components/Ui';
import { ease, glide, inOut, OUT, pop } from '../lib/anim';
import { BeatTimes, useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';

/** A settings section drawn like K-Desktop's (for the ones without a screenshot), in the screenshot's pixels. */
const Panel: React.FC<{ title: string; show: number; children: React.ReactNode }> = ({ title, show, children }) => (
  <div style={{
    position: 'absolute', left: 553, top: 78, width: 892, minHeight: 452, padding: '16px 18px', borderRadius: 8, background: '#2b2b2f', boxSizing: 'border-box',
    opacity: show, transform: `translateY(${(1 - show) * 20}px)`, boxShadow: '0 20px 50px rgba(0,0,0,0.4)',
  }}>
    <div style={{ fontFamily: SANS, fontSize: 17, fontWeight: 600, color: C.text, marginBottom: 14 }}>{title}</div>
    {children}
  </div>
);

const Row: React.FC<{ label: string; hint?: string; lit?: number; children?: React.ReactNode }> = ({ label, hint, lit = 0, children }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '8px 8px', margin: '0 -8px 6px', borderRadius: 6, background: `rgba(242,193,74,${0.13 * lit})`, outline: lit > 0.1 ? `1.5px solid rgba(242,193,74,${0.7 * lit})` : 'none' }}>
    <div style={{ flex: 1 }}>
      <div style={{ fontFamily: SANS, fontSize: 14, fontWeight: 600, color: C.text }}>{label}</div>
      {hint && <div style={{ fontFamily: SANS, fontSize: 12, color: C.dim, marginTop: 2 }}>{hint}</div>}
    </div>
    {children}
  </div>
);

const Chip: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <span style={{ fontFamily: SANS, fontSize: 13, fontWeight: 600, padding: '6px 10px', borderRadius: 6, background: '#3a3a40', color: C.text }}>{children}</span>
);

const Button: React.FC<{ children: React.ReactNode; gold?: boolean; icon?: string }> = ({ children, gold, icon }) => (
  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontFamily: SANS, fontSize: 13, fontWeight: 600, padding: '7px 12px', borderRadius: 6, background: gold ? '#FFD970' : '#3a3a40', color: gold ? '#2a1d02' : C.text }}>
    {icon && <Fluent name={icon} size={14} color={gold ? '#2a1d02' : C.text} />}{children}
  </span>
);

/** Nav items of settings-appearance.png, in its pixels. */
const NAV = { search: [261, 124, 272, 36], general: [261, 259, 260, 38], disable: [261, 455, 260, 40], launcher: [261, 358, 260, 34], voice: [261, 547, 260, 36], multimedia: [261, 597, 260, 36], remote: [261, 686, 260, 36], web: [261, 735, 260, 36], groups: [261, 180, 272, 870] } as const;
const r = (k: keyof typeof NAV) => [...NAV[k]] as [number, number, number, number];

const Main: React.FC<{ intro: BeatTimes; appearance: BeatTimes; general: BeatTimes; disable: BeatTimes }> = ({ intro, appearance, general, disable }) => {
  const f = useCurrentFrame();
  const searchTyped = 'backup'.slice(0, Math.max(0, Math.floor((f - intro.cue('search') - 10) / 3)));
  const themes = ['Light', 'Dark', 'System'];
  const theme = themes[Math.floor(Math.max(0, f - appearance.cue('theme')) / 12) % 3];
  const generalShow = ease(f, general.from - 6, general.from + 8, OUT) * (1 - ease(f, disable.from - 6, disable.from + 2));
  const disableShow = ease(f, disable.from - 6, disable.from + 8, OUT);
  const lit = (at: number, until: number) => inOut(f, at - 4, until, 6);
  return (
    <AbsoluteFill>
      <CaptureShot name="settings-appearance"
        shots={[
          { at: 0, x: 960, y: 540, s: 1.0, rx: 6 },
          { at: intro.from, dur: 40, x: 760, y: 420, s: 1.3, rx: 0 },
          { at: intro.cue('search') - 10, dur: 26, x: 600, y: 300, s: 1.7 },
          { at: appearance.from - 6, dur: 30, x: 1000, y: 330, s: 1.45 },
          { at: general.from - 6, dur: 30, x: 880, y: 400, s: 1.3 },
          { at: disable.from - 6, dur: 30, x: 860, y: 360, s: 1.35 },
        ]}
        focus={[
          { from: intro.cue('settings') - 4, to: intro.cue('search') - 4, rect: r('groups'), label: 'Grouped by topic', at: 'right' },
          { from: intro.cue('search') - 4, to: appearance.from - 4, rect: r('search'), label: 'Ctrl + F', at: 'right' },
          { from: appearance.cue('theme') - 4, to: appearance.cue('languages') - 4, rect: [571, 136, 860, 44], label: theme, at: 'bottom' },
          { from: appearance.cue('languages') - 4, to: appearance.cue('own') - 4, rect: [571, 294, 860, 42], label: 'ES · EN · FR · DE · IT · PT', at: 'bottom' },
          { from: appearance.cue('own') - 4, to: general.from - 4, rect: [573, 406, 856, 118], label: 'nl.json, ja.json…', at: 'bottom' },
          { from: general.from - 4, to: disable.from - 4, rect: r('general') },
          { from: disable.from - 4, to: disable.to + 30, rect: r('disable') },
        ]}>
        <div style={{ position: 'absolute', left: 292, top: 131, fontFamily: SANS, fontSize: 14, color: C.text, background: '#2d2d32', width: 230, opacity: searchTyped ? 1 : 0 }}>{searchTyped}</div>
        <Panel title="General" show={generalShow}>
          <Row label="Start with Windows" hint="Uses a scheduled task: starts with administrator rights without a prompt." lit={lit(general.cue('windows'), general.cue('hotkeys'))}><Toggle on={1} scale={0.9} /></Row>
          <Row label="Show the window on start" hint="Off: K-Desktop starts in the background (tray)."><Toggle on={0} scale={0.9} /></Row>
          <Row label="Tray icon" hint="Always shown while the window is open."><Toggle on={1} scale={0.9} /></Row>
          <Row label="Hotkey to open K-Desktop" lit={lit(general.cue('hotkeys'), general.cue('sounds'))}><Chip>CTRL + ALT + Home</Chip></Row>
          <Row label="Hotkey to exit K-Desktop" lit={lit(general.cue('hotkeys'), general.cue('sounds'))}><Chip>CTRL + ALT + End</Chip></Row>
          <Row label="Pause hotkey" hint="Pauses K-Desktop or resumes it. It works while paused too." lit={lit(general.cue('hotkeys'), general.cue('sounds'))}><Chip>CTRL + ALT + Pause</Chip></Row>
          <Row label="Startup sound" lit={lit(general.cue('sounds'), general.cue('osd'))}><Chip>Chimes ▾</Chip></Row>
          <Row label="On-screen messages" hint="Position and duration of the notices" lit={lit(general.cue('osd'), general.to + 10)}><Chip>Bottom center · 2 s</Chip></Row>
        </Panel>
        <Panel title="Disable K-Desktop in" show={disableShow}>
          <div style={{ fontFamily: SANS, fontSize: 12, color: C.dim, marginBottom: 12 }}>In these applications (games, for example) hotkeys do nothing, except commands set to always work.</div>
          {['eldenring.exe', 'cs2.exe', 'RocketLeague.exe'].map((g, i) => (
            <div key={g} style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 10px', marginBottom: 6, borderRadius: 6, background: 'rgba(255,255,255,0.05)', opacity: ease(f, disable.cue('games') - 6 + i * 5, disable.cue('games') + 4 + i * 5) }}>
              <Fluent name="xbox_controller_24_filled" size={18} color={C.accentText} />
              <span style={{ fontFamily: SANS, fontSize: 14, color: C.text }}>{g}</span>
              <span style={{ marginLeft: 'auto' }}><Fluent name="dismiss_24_regular" size={14} color={C.dim} /></span>
            </div>
          ))}
          <div style={{ marginTop: 10 }}><Button icon="add_24_regular">Add</Button></div>
          <div style={{ marginTop: 18, display: 'flex', alignItems: 'center', gap: 12, padding: '10px 12px', borderRadius: 8, background: 'rgba(242,193,74,0.12)', outline: '1.5px solid rgba(242,193,74,0.6)', opacity: ease(f, disable.cue('always') - 6, disable.cue('always') + 6) }}>
            <KdIcon icon="mic_off_24_filled" color="red" size={26} />
            <span style={{ fontFamily: SANS, fontSize: 14, color: C.text }}>“Mute microphone” · <b style={{ color: C.accentText }}>Always run</b>: it still works in games</span>
          </div>
        </Panel>
      </CaptureShot>
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 120, display: 'flex', justifyContent: 'center', gap: 16, opacity: inOut(f, intro.cue('search') - 6, intro.cue('search') + 30, 6) }}>
        <Keycap label="Ctrl" height={92} width={150} size={30} down={inOut(f, intro.cue('search'), intro.cue('search') + 12, 3)} />
        <Keycap label="F" height={92} size={30} down={inOut(f, intro.cue('search') + 2, intro.cue('search') + 12, 3)} />
      </div>
      <Sfx at={intro.cue('search')} name="key" volume={0.4} />
      <Sfx at={general.from - 6} name="whoosh-short" volume={0.2} />
      <Sfx at={disable.from - 6} name="whoosh-short" volume={0.2} />
    </AbsoluteFill>
  );
};

/** A quick tour of the other sections, on the real screenshots. */
const Sections: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const parts: Array<{ from: number; name: string; shot: { x: number; y: number; s: number }; label: string }> = [
    { from: b.cue('launcher'), name: 'settings-launcher', shot: { x: 900, y: 330, s: 1.45 }, label: 'Launcher' },
    { from: b.cue('voice'), name: 'settings-voice', shot: { x: 1000, y: 420, s: 1.3 }, label: 'Voice' },
    { from: b.cue('web'), name: 'settings-web', shot: { x: 900, y: 330, s: 1.45 }, label: 'Web app' },
  ];
  const cur = parts.reduce((c, p, i) => (f >= p.from - 8 ? i : c), 0);
  const chips = [
    { cue: 'launcher', icon: 'search_24_filled', color: 'yellow', text: 'Launcher' },
    { cue: 'voice', icon: 'mic_24_filled', color: 'teal', text: 'Voice' },
    { cue: 'multimedia', icon: 'music_note_2_24_filled', color: 'pink', text: 'Multimedia' },
    { cue: 'captures', icon: 'screenshot_24_filled', color: 'green', text: 'Captures & magnifier' },
    { cue: 'web', icon: 'phone_24_filled', color: 'sky', text: 'Web app' },
    { cue: 'remote', icon: 'plug_connected_24_filled', color: 'pink', text: 'Remote control' },
  ];
  return (
    <AbsoluteFill>
      {parts.map((p, i) => {
        if (Math.abs(i - cur) > 1) return null;
        const k = i === 0 ? 1 : ease(f, p.from - 8, p.from + 6, OUT);
        const out = i < parts.length - 1 ? ease(f, parts[i + 1].from - 8, parts[i + 1].from + 6, OUT) : 0;
        if (k <= 0 || out >= 1) return null;
        return (
          <AbsoluteFill key={p.name} style={{ opacity: k * (1 - out), transform: `translateX(${(1 - k) * 200 - out * 200}px)` }}>
            <CaptureShot name={p.name} shots={[{ at: p.from - 8, ...p.shot, s: p.shot.s * 0.9 }, { at: p.from, dur: 60, ...p.shot }]} />
          </AbsoluteFill>
        );
      })}
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 110, display: 'flex', justifyContent: 'center', gap: 14 }}>
        {chips.map(c => {
          const k = pop(f, b.cue(c.cue) - 6);
          return (
            <div key={c.cue} style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '12px 20px', borderRadius: 30, background: 'rgba(28,28,34,0.96)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 2), transform: `translateY(${(1 - k) * 40}px)` }}>
              <KdIcon icon={c.icon} color={c.color} size={34} />
              <span style={{ fontFamily: SANS, fontSize: 22, fontWeight: 700, color: C.text }}>{c.text}</span>
            </div>
          );
        })}
      </div>
      {chips.map(c => <Sfx key={c.cue} at={b.cue(c.cue) - 6} name="blip" volume={0.15} />)}
    </AbsoluteFill>
  );
};

/** Backups, export and import, K-Desktop 4, updates. */
const Data: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const lit = (at: number, until: number) => inOut(f, at - 4, until, 6);
  return (
    <CaptureShot name="settings-appearance"
      shots={[{ at: b.from - 8, x: 1000, y: 330, s: 1.3 }, { at: b.from + 10, dur: 120, x: 1000, y: 360, s: 1.42 }]}>
      <Panel title="Data" show={glide(f, b.from - 8, 20)}>
        <Row label="Automatic backups" hint="The same full backup as «Export». Safer on another disk, OneDrive or a NAS." lit={lit(b.cue('daily'), b.cue('export'))}>
          <Button icon="arrow_sync_24_regular">Back up now</Button>
        </Row>
        <div style={{ display: 'flex', gap: 18, margin: '4px 0 14px', fontFamily: SANS, fontSize: 13, color: C.dim }}>
          <span>Every (days) <Chip>3</Chip></span><span>Keep the last <Chip>10</Chip></span><span>Folder <Chip>D:\OneDrive\Backups</Chip></span>
        </div>
        <div style={{ fontFamily: SANS, fontSize: 13, color: C.success, marginBottom: 14, opacity: ease(f, b.cue('zip') - 4, b.cue('zip') + 6) }}>✓ Last backup: today, 09:00 · K-Desktop-2026-10-24.zip</div>
        <Row label="Export · Import" hint="Settings, commands, QuickMenus, macros, K-Board, achievements, wake words, icons, sounds and languages." lit={lit(b.cue('export'), b.cue('import'))}>
          <Button>Export</Button><Button>Import</Button>
        </Row>
        <Row label="Re-import from K-Desktop 4" hint="Your old commands, QuickMenus and icons." lit={lit(b.cue('import'), b.cue('updates'))}><Button icon="arrow_import_24_regular">Re-import</Button></Row>
        <Row label="Updates" hint="K-Desktop 5.0.188 · you have the latest version" lit={lit(b.cue('updates'), b.to + 30)}><Button gold icon="arrow_download_24_regular">Check now</Button></Row>
      </Panel>
    </CaptureShot>
  );
};

export const Settings: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const sections = B('sections');
  const data = B('data');
  return (
    <AbsoluteFill>
      <Part from={0} to={sections.from - 6} first><Main intro={B('intro')} appearance={B('appearance')} general={B('general')} disable={B('disable')} /></Part>
      <Part from={sections.from - 6} to={data.from - 6}><Sections b={sections} /></Part>
      <Part from={data.from - 6} to={durationInFrames} last><Data b={data} /></Part>
    </AbsoluteFill>
  );
};
