import React from 'react';
import { useCurrentFrame } from 'remotion';
import { Fluent, KdIcon } from '../../components/Icon';
import { Keycap, Mono } from '../../components/Keys';
import { Osd } from '../../components/Osd';
import { MAIN_MENU, QuickMenuPanel } from '../../components/QuickMenu';
import { Toggle } from '../../components/Ui';
import { ease, inOut, keys, OUT, pop, rand } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, MONO, SANS } from '../../theme';
import { t as tr } from '../../lib/i18n';

const W = 400;
const H = 176;

/** One small animated card of a family's showcase. */
const Mini: React.FC<{ at: number; title: string; icon: string; color: string; span?: number; h?: number; children: React.ReactNode }> = ({ at, title, icon, color, span = 1, h = H, children }) => {
  const f = useCurrentFrame();
  const k = pop(f, at - 6, { damping: 14, stiffness: 150 });
  const lit = inOut(f, at - 4, at + 40, 8);
  return (
    <div style={{
      width: W * span + (span - 1) * 20, height: h, borderRadius: 18, position: 'relative', overflow: 'hidden', boxSizing: 'border-box',
      background: 'rgba(30,30,36,0.94)', border: `1.5px solid ${lit > 0.05 ? `rgba(242,193,74,${0.25 + 0.6 * lit})` : C.line2}`,
      opacity: Math.min(1, k * 1.6), transform: `translateY(${(1 - k) * 50}px) scale(${0.85 + 0.15 * k})`,
      boxShadow: `0 16px 40px rgba(0,0,0,0.4)${lit > 0.05 ? `, 0 0 40px rgba(242,193,74,${0.25 * lit})` : ''}`,
    }}>
      <div style={{ position: 'absolute', left: 16, top: 14, display: 'flex', alignItems: 'center', gap: 10, zIndex: 2 }}>
        <KdIcon icon={icon} color={color} size={30} />
        <span style={{ fontFamily: SANS, fontSize: 18, fontWeight: 700, color: C.text }}>{title}</span>
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 54, bottom: 0 }}>{children}</div>
    </div>
  );
};

const Grid: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 20, width: W * 2 + 20, alignContent: 'flex-start' }}>{children}</div>
);

const Center: React.FC<{ children: React.ReactNode; style?: React.CSSProperties }> = ({ children, style }) => (
  <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 14, ...style }}>{children}</div>
);

const Typed: React.FC<{ text: string; at: number; speed?: number; size?: number; color?: string }> = ({ text, at, speed = 1.2, size = 18, color = C.text }) => {
  const f = useCurrentFrame();
  const n = Math.max(0, Math.min(text.length, Math.floor((f - at) * speed)));
  return <Mono size={size} color={color}>{text.slice(0, n)}<span style={{ opacity: f % 20 < 10 ? 1 : 0 }}>▍</span></Mono>;
};

/** A tiny monitor with one window, for the window layouts. */
const Monitor: React.FC<{ w: number; h: number; win: { x: number; y: number; w: number; h: number }; opacity?: number; pin?: number }> = ({ w, h, win, opacity = 1, pin = 0 }) => (
  <div style={{ position: 'relative', width: w, height: h + 18 }}>
    <div style={{ position: 'absolute', left: 0, top: 0, width: w, height: h, borderRadius: 8, background: 'linear-gradient(135deg,#1c2a5a,#3a1f6a)', border: '3px solid #3a3a44', overflow: 'hidden' }}>
      <div style={{
        position: 'absolute', left: win.x * w, top: win.y * h, width: win.w * w, height: win.h * h, borderRadius: 4, opacity,
        background: '#2c2e36', border: `1.5px solid ${C.accentText}`, boxShadow: '0 4px 12px rgba(0,0,0,0.5)', boxSizing: 'border-box',
      }}>
        <div style={{ height: 8, background: 'rgba(242,193,74,0.5)' }} />
      </div>
      {pin > 0 && <div style={{ position: 'absolute', right: 6, top: 6, transform: `scale(${pin})` }}><Fluent name="pin_24_filled" size={20} color={C.accentText} /></div>}
    </div>
    <div style={{ position: 'absolute', left: w / 2 - 16, top: h, width: 32, height: 14, background: '#3a3a44', borderRadius: '0 0 4px 4px' }} />
  </div>
);

// ── the families ────────────────────────────────────────────────────

export const AppsDemo: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const browsers = ['Chrome', 'Firefox', 'Edge', 'Brave', 'Opera', 'Vivaldi'];
  const pick = Math.floor(keys(f, [[b.cue('web'), 0], [b.cue('web') + 30, 3]]));
  const special = ['This PC', 'Recycle Bin', 'Control Panel', 'God Mode', 'Task View', 'Fonts'];
  return (
    <Grid>
      <Mini at={b.cue('programs')} title={tr('Run application')} icon="open_24_filled" color="blue">
        <Center>
          <div style={{ width: 230, height: 96, borderRadius: 8, background: '#2a2c34', border: `1px solid ${C.line2}`, transform: `scale(${pop(f, b.cue('programs'))})`, padding: 10, boxSizing: 'border-box' }}>
            <Mono size={14} color={C.dim}>{tr('notepad.exe')}</Mono>
            <div style={{ marginTop: 10, display: 'flex', gap: 8 }}>
              <span style={{ fontFamily: SANS, fontSize: 13, padding: '4px 10px', borderRadius: 6, background: C.accentSoft, color: C.accentText }}>{tr('As user')}</span>
              <span style={{ fontFamily: SANS, fontSize: 13, padding: '4px 10px', borderRadius: 6, background: 'rgba(255,255,255,0.08)', color: C.dim }}>{tr('As admin')}</span>
            </div>
          </div>
        </Center>
      </Mini>
      <Mini at={b.cue('folders')} title={tr('Open folder')} icon="folder_open_24_filled" color="yellow">
        <div style={{ position: 'absolute', left: 16, right: 16, top: 6, display: 'flex', flexWrap: 'wrap', gap: 8 }}>
          {special.map((s, i) => {
            const k = pop(f, b.cue('special') + i * 4);
            return <span key={s} style={{ fontFamily: SANS, fontSize: 15, padding: '6px 12px', borderRadius: 16, background: 'rgba(255,255,255,0.08)', color: C.text, opacity: k, transform: `scale(${k})` }}>{s}</span>;
          })}
          <span style={{ fontFamily: MONO, fontSize: 14, color: C.dim, marginTop: 4, opacity: 1 - ease(f, b.cue('special') - 4, b.cue('special')) }}>{tr('C:\\Users\\you\\Documents')}</span>
        </div>
      </Mini>
      <Mini at={b.cue('web')} title={tr('Open web page')} icon="globe_24_filled" color="sky">
        <div style={{ position: 'absolute', left: 16, right: 16, top: 6, display: 'flex', flexWrap: 'wrap', gap: 8 }}>
          {browsers.map((s, i) => (
            <span key={s} style={{ fontFamily: SANS, fontSize: 15, padding: '6px 12px', borderRadius: 16, background: i === pick ? C.accentText : 'rgba(255,255,255,0.08)', color: i === pick ? '#1a1306' : C.text, fontWeight: i === pick ? 700 : 400 }}>{s}</span>
          ))}
          <span style={{ fontFamily: SANS, fontSize: 14, color: C.dim }}>{tr('New window · Incognito')}</span>
        </div>
      </Mini>
      <Mini at={b.cue('scripts')} title={tr('Run script')} icon="window_console_20_filled" color="dark">
        <div style={{ position: 'absolute', left: 16, right: 16, top: 4, bottom: 14, borderRadius: 8, background: '#0c0c10', padding: '10px 12px', boxSizing: 'border-box' }}>
          <Mono size={13} color="#5AA9FF">{tr('PS&gt;')}{' '}</Mono><Typed text={tr('Get-Process | Sort CPU -Desc')} at={b.cue('scripts')} size={13} />
        </div>
      </Mini>
    </Grid>
  );
};

export const WindowsDemo: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const layouts: Array<[string, { x: number; y: number; w: number; h: number }]> = [
    ['max', { x: 0, y: 0, w: 1, h: 1 }], ['halves', { x: 0, y: 0, w: 0.5, h: 1 }], ['quarters', { x: 0.5, y: 0, w: 0.5, h: 0.5 }],
    ['thirds', { x: 0.333, y: 0, w: 0.334, h: 1 }], ['center', { x: 0.2, y: 0.15, w: 0.6, h: 0.7 }],
  ];
  let win = { x: 0.15, y: 0.15, w: 0.5, h: 0.55 };
  for (const [cue, l] of layouts) {
    const k = ease(f, b.cue(cue) - 4, b.cue(cue) + 8, OUT);
    win = { x: win.x + (l.x - win.x) * k, y: win.y + (l.y - win.y) * k, w: win.w + (l.w - win.w) * k, h: win.h + (l.h - win.h) * k };
  }
  const label = layouts.reduce((s, [cue]) => (f >= b.cue(cue) - 4 ? cue : s), '');
  const names: Record<string, string> = { max: tr('Maximize'), halves: tr('Left half'), quarters: tr('Top right quarter'), thirds: tr('Middle third'), center: tr('Centre') };
  const across = ease(f, b.cue('monitor'), b.cue('monitor') + 16, OUT);
  return (
    <Grid>
      <Mini at={b.cue('max')} title={tr('Window')} icon="window_new_24_filled" color="sky" span={2} h={300}>
        <Center style={{ flexDirection: 'column', gap: 10 }}>
          <Monitor w={340} h={190} win={win} />
          <span style={{ fontFamily: SANS, fontSize: 20, fontWeight: 700, color: C.accentText }}>{names[label] ?? ''}</span>
        </Center>
      </Mini>
      <Mini at={b.cue('monitor')} title={tr('To another monitor')} icon="desktop_24_filled" color="blue">
        <Center>
          <Monitor w={140} h={80} win={{ x: 0.2 + across * 1.4, y: 0.2, w: 0.6, h: 0.6 }} />
          <Monitor w={140} h={80} win={{ x: -1.2 + across * 1.4, y: 0.2, w: 0.6, h: 0.6 }} />
        </Center>
      </Mini>
      <Mini at={b.cue('transparent')} title={tr('Transparent · on top')} icon="pin_24_filled" color="orange">
        <Center>
          <Monitor w={200} h={110} win={{ x: 0.2, y: 0.15, w: 0.6, h: 0.7 }} opacity={1 - 0.6 * inOut(f, b.cue('transparent'), b.cue('top'), 8)} pin={pop(f, b.cue('top'))} />
        </Center>
      </Mini>
    </Grid>
  );
};

export const InputDemo: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const upper = f > b.cue('clipboard') + 14;
  const caps = inOut(f, b.cue('kboard'), b.cue('kboard') + 40, 6);
  return (
    <Grid>
      <Mini at={b.cue('keys')} title={tr('Send keys / text')} icon="keyboard_24_filled" color="indigo">
        <Center style={{ flexDirection: 'column', gap: 12 }}>
          <div style={{ display: 'flex', gap: 10 }}>
            <Keycap label={tr('Ctrl')} height={44} width={70} size={16} down={inOut(f, b.cue('keys'), b.cue('keys') + 10, 3)} />
            <Keycap label={tr('Shift')} height={44} width={70} size={16} down={inOut(f, b.cue('keys') + 2, b.cue('keys') + 10, 3)} />
            <Keycap label="T" height={44} size={16} down={inOut(f, b.cue('keys') + 4, b.cue('keys') + 10, 3)} />
          </div>
          <Typed text={tr('Best regards, K.')} at={b.cue('keys') + 12} size={16} />
        </Center>
      </Mini>
      <Mini at={b.cue('clipboard')} title={tr('Clipboard')} icon="clipboard_text_edit_24_filled" color="brown">
        <Center style={{ flexDirection: 'column', gap: 8 }}>
          <Mono size={22} color={upper ? C.accentText : C.text}>{upper ? tr('HELLO WORLD') : tr('hello world')}</Mono>
          <span style={{ fontFamily: SANS, fontSize: 14, color: C.dim }}>{tr('Plain · UPPER · lower · clean links · date & time')}</span>
        </Center>
      </Mini>
      <Mini at={b.cue('macro')} title={tr('Play macro')} icon="record_24_filled" color="red">
        <Center>
          {Array.from({ length: 9 }, (_, i) => {
            const on = f > b.cue('macro') + i * 4;
            return <div key={i} style={{ width: 26, height: 14 + 30 * rand(i + 3), borderRadius: 5, background: on ? C.accentText : 'rgba(255,255,255,0.15)' }} />;
          })}
        </Center>
      </Mini>
      <Mini at={b.cue('kboard')} title={tr('K-Board')} icon="keyboard_layout_float_24_filled" color="purple">
        <Center>
          <Keycap label={tr('Caps Lock')} height={50} width={120} size={16} lit={caps} />
          <Fluent name="arrow_right_24_filled" size={28} color={C.accentText} />
          <Keycap label={tr('Ctrl')} height={50} width={90} size={16} />
        </Center>
      </Mini>
      <Mini at={b.cue('dictate')} title={tr('Dictate')} icon="mic_sparkle_24_filled" color="teal" span={2} h={110}>
        <div style={{ position: 'absolute', left: 20, top: 0, display: 'flex', alignItems: 'center', gap: 14 }}>
          <Fluent name="mic_24_filled" size={26} color={C.teal} />
          <Typed text={tr('Meeting moved to Thursday at ten.')} at={b.cue('dictate') + 4} speed={1.4} size={17} />
        </div>
      </Mini>
    </Grid>
  );
};

export const SoundDemo: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const v = (i: number) => keys(f, [[b.cue('volume'), 70], [b.cue('volume') + 20 + i * 6, [30, 85, 45][i]], [b.cue('output'), [40, 75, 55][i]]]);
  const out = f > b.cue('output') + 8;
  return (
    <Grid>
      <Mini at={b.cue('volume')} title={tr('Volume')} icon="speaker_2_24_filled" color="teal" span={2} h={210}>
        <div style={{ position: 'absolute', left: 24, right: 24, top: 6, display: 'flex', flexDirection: 'column', gap: 18 }}>
          {[tr('Speakers'), tr('Microphone'), 'Spotify.exe'].map((n, i) => (
            <div key={n} style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
              <span style={{ width: 150, fontFamily: SANS, fontSize: 17, color: C.text }}>{n}</span>
              <div style={{ flex: 1, height: 6, borderRadius: 3, background: 'rgba(255,255,255,0.12)', position: 'relative' }}>
                <div style={{ width: `${v(i)}%`, height: '100%', borderRadius: 3, background: C.teal }} />
                <div style={{ position: 'absolute', left: `${v(i)}%`, top: -7, width: 20, height: 20, marginLeft: -10, borderRadius: 10, background: 'white' }} />
              </div>
              <span style={{ width: 50, fontFamily: SANS, fontSize: 16, color: C.dim, textAlign: 'right' }}>{Math.round(v(i))}</span>
            </div>
          ))}
        </div>
      </Mini>
      <Mini at={b.cue('output')} title={tr('Audio output')} icon="headphones_sound_wave_24_filled" color="purple">
        <Center>
          {[tr('Speakers'), tr('Headphones')].map((n, i) => (
            <div key={n} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '10px 14px', borderRadius: 12, background: (i === 1) === out ? C.accentSoft : 'rgba(255,255,255,0.06)', border: `1px solid ${(i === 1) === out ? C.accentText : 'transparent'}` }}>
              <Fluent name={i ? 'headphones_24_filled' : 'speaker_2_24_filled'} size={22} color={(i === 1) === out ? C.accentText : C.dim} />
              <span style={{ fontFamily: SANS, fontSize: 16, color: C.text }}>{n}</span>
            </div>
          ))}
        </Center>
      </Mini>
      <Mini at={b.cue('record')} title={tr('Record audio')} icon="mic_pulse_24_filled" color="red">
        <Center>
          <div style={{ position: 'relative', display: 'flex', alignItems: 'center', gap: 3 }}>
            {Array.from({ length: 40 }, (_, i) => (
              <div key={i} style={{ width: 4, height: 8 + 40 * Math.abs(Math.sin(i * 0.9 + f / 5)) * rand(i), borderRadius: 2, background: i > 26 ? C.danger : 'rgba(255,255,255,0.3)' }} />
            ))}
            <div style={{ position: 'absolute', right: -6, top: -26, fontFamily: SANS, fontSize: 13, color: C.danger, fontWeight: 700 }}>{tr('last 30 s')}</div>
          </div>
        </Center>
      </Mini>
    </Grid>
  );
};

export const ScreenDemo: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const sel = ease(f, b.cue('shot'), b.cue('shot') + 16, OUT);
  const flash = inOut(f, b.cue('shot') + 18, b.cue('shot') + 26, 3);
  const scan = ease(f, b.cue('ocr'), b.cue('ocr') + 22);
  const dark = f > b.cue('theme') + 10;
  return (
    <Grid>
      <Mini at={b.cue('shot')} title={tr('Screenshot')} icon="screenshot_24_filled" color="green">
        <div style={{ position: 'absolute', left: 20, top: 4, width: 360, height: 100, borderRadius: 8, background: 'linear-gradient(120deg,#22304f,#3b2453)', overflow: 'hidden' }}>
          <div style={{ position: 'absolute', left: 60, top: 18, width: 220 * sel, height: 64 * sel, border: '2px dashed white', background: 'rgba(255,255,255,0.08)' }} />
          <div style={{ position: 'absolute', inset: 0, background: `rgba(255,255,255,${flash * 0.8})` }} />
        </div>
      </Mini>
      <Mini at={b.cue('ocr')} title={tr('Copy text from the screen')} icon="scan_text_24_filled" color="lime">
        <div style={{ position: 'absolute', left: 20, top: 4, width: 360, height: 100 }}>
          {['Invoice 2026-118', 'Total due: 248,00 €', 'IBAN ES12 3456 7890'].map((t, i) => (
            <div key={t} style={{ fontFamily: SANS, fontSize: 17, color: scan * 3 > i + 0.5 ? C.accentText : C.dim, marginBottom: 6 }}>{t}</div>
          ))}
          <div style={{ position: 'absolute', left: 0, right: 0, top: scan * 100, height: 2, background: '#C3EB5C', boxShadow: '0 0 10px #C3EB5C', opacity: scan < 1 ? 1 : 0 }} />
        </div>
      </Mini>
      <Mini at={b.cue('magnifier')} title={tr('Magnifier')} icon="zoom_in_24_filled" color="gold">
        <Center>
          <div style={{ width: 96, height: 96, borderRadius: 48, border: `4px solid ${C.accentText}`, overflow: 'hidden', position: 'relative', background: '#1c2a5a', transform: `translateX(${Math.sin(f / 14) * 60}px)` }}>
            <div style={{ position: 'absolute', inset: -40, backgroundImage: 'radial-gradient(rgba(255,255,255,0.5) 3px, transparent 3.5px)', backgroundSize: '22px 22px' }} />
          </div>
        </Center>
      </Mini>
      <Mini at={b.cue('brightness')} title={tr('Screen')} icon="desktop_24_filled" color="blue">
        <Center style={{ flexDirection: 'column', gap: 12 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <Fluent name="weather_sunny_24_filled" size={26} color={C.gold1} />
            <div style={{ width: 220, height: 6, borderRadius: 3, background: 'rgba(255,255,255,0.12)' }}>
              <div style={{ width: `${keys(f, [[b.cue('brightness'), 40], [b.cue('brightness') + 20, 90]])}%`, height: '100%', borderRadius: 3, background: C.gold1 }} />
            </div>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            {['PC only', 'Duplicate', 'Extend'].map((p, i) => <span key={p} style={{ fontFamily: SANS, fontSize: 14, padding: '4px 10px', borderRadius: 8, background: i === 2 ? C.accentSoft : 'rgba(255,255,255,0.06)', color: i === 2 ? C.accentText : C.dim }}>{p}</span>)}
          </div>
        </Center>
      </Mini>
      <Mini at={b.cue('theme')} title={tr('Windows theme')} icon="dark_theme_24_filled" color="dark" span={2} h={110}>
        <div style={{ position: 'absolute', left: 20, right: 20, top: 0, display: 'flex', alignItems: 'center', gap: 16 }}>
          <div style={{ width: 120, height: 44, borderRadius: 8, background: dark ? '#202024' : '#f3f3f3', border: '1px solid rgba(255,255,255,0.2)' }} />
          <Toggle on={dark ? 1 : 0} />
          <span style={{ fontFamily: SANS, fontSize: 17, color: C.text }}>{dark ? tr('Dark') : tr('Light')}</span>
        </div>
      </Mini>
    </Grid>
  );
};

export const NetworkDemo: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const packet = ease(f, b.cue('http') + 6, b.cue('http') + 26);
  const bulb = f > b.cue('http') + 26;
  const wolPulse = ease(f, b.cue('wol') + 4, b.cue('wol') + 24);
  const awake = f > b.cue('wol') + 24;
  return (
    <Grid>
      <Mini at={b.cue('http')} title={tr('Web request')} icon="arrow_swap_24_filled" color="teal" span={2} h={210}>
        <div style={{ position: 'absolute', left: 24, right: 24, top: 10, display: 'flex', alignItems: 'center', gap: 20 }}>
          <div style={{ padding: '10px 14px', borderRadius: 10, background: '#0c0c10', width: 430 }}>
            <Mono size={15} color="#3EDCC1">{tr('POST')}</Mono> <Mono size={15}>{tr('http://homeassistant:8123')}</Mono><br />
            <Mono size={15} color={C.dim}>{tr('/api/services/light/turn_on')}</Mono><br />
            <Mono size={15} color={C.accentText}>{tr('{ "entity_id": "light.desk" }')}</Mono>
          </div>
          <div style={{ flex: 1, height: 3, background: 'rgba(255,255,255,0.15)', position: 'relative' }}>
            <div style={{ position: 'absolute', left: `${packet * 100}%`, top: -6, width: 14, height: 14, borderRadius: 7, background: C.teal, boxShadow: `0 0 14px ${C.teal}`, opacity: packet > 0 && packet < 1 ? 1 : 0 }} />
          </div>
          <div style={{ width: 90, height: 90, borderRadius: 45, display: 'flex', alignItems: 'center', justifyContent: 'center', background: bulb ? 'rgba(255,217,112,0.25)' : 'rgba(255,255,255,0.05)', boxShadow: bulb ? '0 0 50px rgba(255,217,112,0.7)' : 'none' }}>
            <Fluent name="lightbulb_filament_24_filled" size={52} color={bulb ? C.gold1 : C.faint} />
          </div>
        </div>
      </Mini>
      <Mini at={b.cue('wol')} title={tr('Wake a computer')} icon="desktop_pulse_24_filled" color="green">
        <Center>
          <div style={{ width: 120, height: 76, borderRadius: 8, border: '3px solid #3a3a44', background: awake ? 'linear-gradient(135deg,#1c2a5a,#3a1f6a)' : '#08080a', position: 'relative' }}>
            <div style={{ position: 'absolute', right: 6, bottom: 6, width: 8, height: 8, borderRadius: 4, background: awake ? C.success : '#333' }} />
          </div>
          <div style={{ width: 70, height: 70, borderRadius: 35, border: `3px solid ${C.success}`, opacity: 1 - wolPulse, transform: `scale(${0.4 + wolPulse})`, position: 'absolute', left: 140 }} />
        </Center>
      </Mini>
      <Mini at={b.cue('radio')} title={tr('Wi-Fi and Bluetooth')} icon="wifi_1_24_filled" color="sky">
        <Center style={{ gap: 28 }}>
          {[['wifi_1_24_filled', 0], ['bluetooth_24_filled', 8]].map(([ic, d]) => (
            <div key={ic as string} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <Fluent name={ic as string} size={28} color={C.text} />
              <Toggle on={ease(f, b.cue('radio') + (d as number), b.cue('radio') + (d as number) + 6)} />
            </div>
          ))}
        </Center>
      </Mini>
    </Grid>
  );
};

export const SystemDemo: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const shown = ease(f, b.cue('hidden') + 4, b.cue('hidden') + 12);
  const plan = f > b.cue('plan') + 10 ? 1 : 0;
  const t = Math.max(0, f - b.cue('countdown'));
  const secs = Math.max(0, 10 - Math.floor(t / 10));
  return (
    <Grid>
      <Mini at={b.cue('hidden')} title={tr('Hidden files')} icon="eye_off_24_filled" color="gray">
        <Center style={{ gap: 18 }}>
          {[0, 1, 2, 3].map(i => <Fluent key={i} name={i % 2 ? 'document_24_filled' : 'folder_24_filled'} size={46} color={i % 2 ? '#9fb3c8' : '#F2C14A'} style={{ opacity: i === 1 || i === 3 ? 0.25 + 0.3 * shown : 1, transform: `scale(${i === 1 || i === 3 ? shown : 1})` }} />)}
        </Center>
      </Mini>
      <Mini at={b.cue('plan')} title={tr('Power plan')} icon="battery_10_24_filled" color="lime">
        <Center style={{ flexDirection: 'column', gap: 8 }}>
          {[tr('Balanced'), tr('High performance')].map((p, i) => (
            <div key={p} style={{ width: 280, padding: '8px 14px', borderRadius: 10, fontFamily: SANS, fontSize: 16, background: i === plan ? C.accentSoft : 'rgba(255,255,255,0.06)', color: i === plan ? C.accentText : C.dim, border: `1px solid ${i === plan ? C.accentText : 'transparent'}` }}>{p}</div>
          ))}
        </Center>
      </Mini>
      <Mini at={b.cue('power')} title={tr('Power')} icon="power_24_filled" color="red" span={2} h={250}>
        <Center style={{ gap: 40 }}>
          <svg width="150" height="150" viewBox="0 0 150 150">
            <circle cx="75" cy="75" r="62" stroke="rgba(255,255,255,0.1)" strokeWidth="10" fill="none" />
            <circle cx="75" cy="75" r="62" stroke={C.danger} strokeWidth="10" fill="none" strokeDasharray={`${390 * (secs / 10)} 390`} transform="rotate(-90 75 75)" strokeLinecap="round" />
            <text x="75" y="90" textAnchor="middle" fontFamily={SANS} fontSize="46" fontWeight="800" fill="white">{secs}</text>
          </svg>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <div style={{ fontFamily: SANS, fontSize: 24, fontWeight: 700, color: C.text }}>{tr('Shutting down in')}{' '}{secs} s</div>
            <div style={{ display: 'flex', gap: 10 }}>
              {[tr('Shut down'), tr('Restart'), tr('Sleep'), tr('Lock')].map((p, i) => <span key={p} style={{ fontFamily: SANS, fontSize: 15, padding: '5px 10px', borderRadius: 8, background: i === 0 ? 'rgba(255,107,107,0.2)' : 'rgba(255,255,255,0.06)', color: i === 0 ? C.danger : C.dim }}>{p}</span>)}
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, fontFamily: SANS, fontSize: 16, color: C.dim }}>
              <Keycap label={tr('Esc')} height={38} width={60} size={15} down={inOut(f, b.to - 14, b.to - 4, 3)} /> {tr('cancels')}
            </div>
          </div>
        </Center>
      </Mini>
    </Grid>
  );
};

export const FlowDemo: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const wait = ease(f, b.cue('wait'), b.cue('wait') + 30);
  const on = f > b.cue('commands') + 12 ? 0 : 1;
  return (
    <Grid>
      <Mini at={b.cue('message')} title={tr('Show message')} icon="chat_24_filled" color="blue">
        <Center><Osd icon="flash_24_filled" color="gold" text={tr('Backup finished')} sub={tr('2 412 files · 3.1 GB')} scale={0.95} show={pop(f, b.cue('message') + 4)} /></Center>
      </Mini>
      <Mini at={b.cue('wait')} title={tr('Wait')} icon="timer_24_filled" color="purple">
        <Center>
          <svg width="90" height="90" viewBox="0 0 90 90">
            <circle cx="45" cy="45" r="36" stroke="rgba(255,255,255,0.1)" strokeWidth="8" fill="none" />
            <circle cx="45" cy="45" r="36" stroke="#BC8CFF" strokeWidth="8" fill="none" strokeDasharray={`${226 * wait} 226`} transform="rotate(-90 45 45)" strokeLinecap="round" />
          </svg>
          <span style={{ fontFamily: SANS, fontSize: 26, fontWeight: 700, color: C.text }}>{(wait * 1.5).toFixed(1)} s</span>
        </Center>
      </Mini>
      <Mini at={b.cue('menu')} title={tr('Open QuickMenu')} icon="text_bullet_list_tree_24_filled" color="indigo" h={230}>
        <div style={{ position: 'absolute', left: 30, top: -4, transform: 'scale(0.9)', transformOrigin: 'top left' }}>
          <QuickMenuPanel items={MAIN_MENU.slice(0, 5)} scale={1} appear={ease(f, b.cue('menu'), b.cue('menu') + 16)} hot={2} width={300} />
        </div>
      </Mini>
      <Mini at={b.cue('commands')} title={tr('Commands')} icon="flash_24_filled" color="gold" h={230}>
        <div style={{ position: 'absolute', left: 20, right: 20, top: 8, display: 'flex', flexDirection: 'column', gap: 12 }}>
          {[['Run', 'play_24_filled'], ['Enable / disable', 'toggle_left_24_filled']].map(([t, ic], i) => (
            <div key={t} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 14px', borderRadius: 10, background: 'rgba(255,255,255,0.06)' }}>
              <Fluent name={ic} size={22} color={C.accentText} />
              <span style={{ fontFamily: SANS, fontSize: 17, color: C.text }}>{t} {tr('“Night mode”')}</span>
              {i === 1 && <div style={{ marginLeft: 'auto' }}><Toggle on={on} /></div>}
            </div>
          ))}
        </div>
      </Mini>
    </Grid>
  );
};

export const DEMOS: Record<string, React.FC<{ b: BeatTimes }>> = {
  apps: AppsDemo, windows: WindowsDemo, input: InputDemo, sound: SoundDemo, screen: ScreenDemo, network: NetworkDemo, system: SystemDemo, kdesktop: FlowDemo,
};
