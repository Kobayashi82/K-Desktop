import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { Camera } from '../../components/Camera';
import { Kinetic } from '../../components/Diagram';
import { Fluent, KdIcon } from '../../components/Icon';
import { Keycap, Mono } from '../../components/Keys';
import { WinFrame } from '../../components/KdWindow';
import { Sfx } from '../../components/Sfx';
import { Toggle } from '../../components/Ui';
import { ease, glide, inOut, keys, OUT, pop, rand } from '../../lib/anim';
import { BeatTimes } from '../../lib/timeline';
import { C, PALETTE, SANS } from '../../theme';
import { t } from '../../lib/i18n';

const Row: React.FC<{ icon: string; color: string; title: string; detail: string; x: number; y: number; w: number; tone?: 'yes' | 'no' | 'dim' | 'none'; badge?: React.ReactNode; show?: number }> = ({ icon, color, title, detail, x, y, w, tone = 'none', badge, show = 1 }) => (
  <div style={{
    position: 'absolute', left: x, top: y, width: w, height: 84, boxSizing: 'border-box', display: 'flex', alignItems: 'center', gap: 16, padding: '0 22px', borderRadius: 16,
    background: tone === 'yes' ? 'rgba(108,203,95,0.14)' : tone === 'no' ? 'rgba(255,107,107,0.14)' : 'rgba(34,34,40,0.96)',
    border: `1.5px solid ${tone === 'yes' ? 'rgba(108,203,95,0.7)' : tone === 'no' ? 'rgba(255,107,107,0.7)' : C.line2}`,
    opacity: (tone === 'dim' ? 0.35 : 1) * Math.min(1, show * 1.5), transform: `translateX(${(1 - show) * -60}px)`, boxShadow: '0 12px 30px rgba(0,0,0,0.4)',
  }}>
    <KdIcon icon={icon} color={color} size={48} />
    <div>
      <div style={{ fontFamily: SANS, fontSize: 23, fontWeight: 700, color: C.text }}>{title}</div>
      <div style={{ fontFamily: SANS, fontSize: 17, color: C.dim }}>{detail}</div>
    </div>
    <div style={{ marginLeft: 'auto' }}>{badge}</div>
  </div>
);

const Pill: React.FC<{ text: string; color: string; bg: string }> = ({ text, color, bg }) => (
  <span style={{ fontFamily: SANS, fontSize: 18, fontWeight: 700, color, background: bg, padding: '6px 14px', borderRadius: 12 }}>{text}</span>
);

/** «If…»: what it checks, and the two ways a command can go. */
export const IfPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const ifAt = b.cue('if');
  const qs = [
    { cue: 'program', icon: 'window_apps_24_filled', color: 'indigo', text: t('Is Steam open?'), x: 420, y: 260 },
    { cue: 'time', icon: 'clock_24_filled', color: 'blue', text: t('Is it after 22:00?'), x: 1500, y: 260 },
    { cue: 'device', icon: 'usb_stick_24_filled', color: 'gray', text: t('Headphones connected?'), x: 420, y: 800 },
    { cue: 'cpu', icon: 'data_usage_24_filled', color: 'orange', text: t('CPU above 80 %?'), x: 1500, y: 800 },
  ];
  const toFlow = glide(f, b.cue('no') - 16, 26);
  const noAt = b.cue('no');
  const stopAt = b.cue('stop');
  const skipAt = b.cue('skip');
  const pathsAt = b.cue('paths');
  const X = 560;
  const Y0 = 250;
  const rows = [
    { icon: 'branch_fork_24_filled', color: 'orange', title: t('If…'), detail: t('Steam.exe is running') },
    { icon: 'headphones_sound_wave_24_filled', color: 'purple', title: t('Audio output'), detail: t('Headphones') },
    { icon: 'speaker_2_24_filled', color: 'teal', title: t('Volume'), detail: '40 %' },
    { icon: 'chat_24_filled', color: 'blue', title: t('Show message'), detail: t('“Have fun!”') },
  ];
  const mode = f < stopAt ? 'eval' : f < skipAt ? 'stop' : f < pathsAt ? 'skip' : 'paths';
  // The token running down the command.
  const tokenY = (() => {
    if (mode === 'stop') return Y0 + 42;
    if (mode === 'skip') return keys(f, [[skipAt, Y0 + 42], [skipAt + 26, Y0 + 3 * 110 + 42]]);
    return Y0 + 42;
  })();
  const tokenX = mode === 'skip' ? X - 40 - Math.sin(Math.PI * ease(f, skipAt, skipAt + 26)) * 120 : X - 40;
  return (
    <AbsoluteFill>
      {/* The question */}
      <AbsoluteFill style={{ opacity: 1 - toFlow, transform: `scale(${1 - 0.15 * toFlow})` }}>
        <div style={{ position: 'absolute', left: 960, top: 520, transform: `translate(-50%, -50%) scale(${pop(f, ifAt - 6, { damping: 11 })})` }}>
          <KdIcon icon="branch_fork_24_filled" color="orange" size={200} glow={0.5} />
        </div>
        <Kinetic text={t('If…')} at={ifAt} x={960} y={720} size={96} />
        {qs.map(q => {
          const k = pop(f, b.cue(q.cue) - 6, { damping: 13, stiffness: 150 });
          return (
            <div key={q.cue} style={{
              position: 'absolute', left: q.x, top: q.y, transform: `translate(-50%, -50%) scale(${k}) translateY(${Math.sin(f / 18 + q.x) * 8}px)`, opacity: k,
              display: 'flex', alignItems: 'center', gap: 14, padding: '16px 24px', borderRadius: 40, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${C.line2}`,
            }}>
              <KdIcon icon={q.icon} color={q.color} size={44} />
              <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{q.text}</span>
            </div>
          );
        })}
      </AbsoluteFill>

      {/* The command and its paths */}
      <AbsoluteFill style={{ opacity: toFlow, transform: `scale(${0.9 + 0.1 * toFlow})` }}>
        {rows.map((r, i) => {
          const inBlock = i === 1 || i === 2;
          const tone = i === 0 ? (f > noAt ? 'no' : 'none')
            : mode === 'stop' ? 'dim'
              : mode === 'skip' && inBlock ? 'dim'
                : 'none';
          return (
            <Row key={i} {...r} x={X + (inBlock ? 70 : 0)} y={Y0 + i * 110} w={800 - (inBlock ? 70 : 0)} tone={tone as 'no' | 'dim' | 'none'} show={glide(f, noAt - 16 + i * 3, 16)}
              badge={i === 0 && f > noAt ? <Pill text={mode === 'paths' ? t('Yes / No') : t('No')} color={mode === 'paths' ? C.text : C.danger} bg={mode === 'paths' ? 'rgba(255,255,255,0.1)' : 'rgba(255,107,107,0.18)'} />
                : i === 0 ? <Pill text={t('checks…')} color={C.dim} bg="rgba(255,255,255,0.06)" /> : undefined} />
          );
        })}
        {/* The block the «If…» governs */}
        <div style={{ position: 'absolute', left: X + 30, top: Y0 + 110, width: 6, height: 194, borderRadius: 3, background: C.accentText, opacity: 0.6 * toFlow }} />
        {mode === 'stop' && (
          <div style={{ position: 'absolute', left: X + 840, top: Y0 + 4, transform: `scale(${pop(f, stopAt)})`, display: 'flex', alignItems: 'center', gap: 12 }}>
            <Fluent name="stop_24_filled" size={64} color={C.danger} />
            <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.danger }}>{t('Stop')}</span>
          </div>
        )}
        {mode === 'skip' && (
          <div style={{ position: 'absolute', left: X + 840, top: Y0 + 150, opacity: ease(f, skipAt, skipAt + 10), display: 'flex', alignItems: 'center', gap: 12 }}>
            <Fluent name="arrow_hook_down_right_24_filled" size={60} color={C.warning} />
            <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.warning }}>{t('Skip 2')}</span>
          </div>
        )}
        {mode !== 'paths' && f > noAt - 10 && (
          <div style={{ position: 'absolute', left: tokenX - 14, top: tokenY - 14, width: 28, height: 28, borderRadius: 14, background: C.gold1, boxShadow: `0 0 24px ${C.gold1}` }} />
        )}
        {mode === 'paths' && (
          <svg style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible' }} width={1} height={1}>
            {(() => {
              const d1 = `M ${X - 40} ${Y0 + 42} L ${X - 40} ${Y0 + 3 * 110 + 42}`;
              const d2 = `M ${X - 40} ${Y0 + 42} C ${X - 220} ${Y0 + 120}, ${X - 220} ${Y0 + 300}, ${X - 40} ${Y0 + 3 * 110 + 42}`;
              const k = ease(f, pathsAt, pathsAt + 24, OUT);
              return (
                <>
                  <path d={d1} stroke={C.success} strokeWidth={6} fill="none" strokeDasharray={`${400 * k} 400`} strokeLinecap="round" />
                  <path d={d2} stroke={C.warning} strokeWidth={6} fill="none" strokeDasharray={`${560 * k} 560`} strokeLinecap="round" />
                </>
              );
            })()}
          </svg>
        )}
        {mode === 'paths' && (
          <>
            <div style={{ position: 'absolute', left: X - 30, top: Y0 + 190, opacity: ease(f, pathsAt + 10, pathsAt + 20) }}><Pill text={t('Yes')} color={C.success} bg="rgba(108,203,95,0.18)" /></div>
            <div style={{ position: 'absolute', left: X - 290, top: Y0 + 190, opacity: ease(f, pathsAt + 10, pathsAt + 20) }}><Pill text={t('No')} color={C.warning} bg="rgba(252,179,63,0.18)" /></div>
          </>
        )}
      </AbsoluteFill>
      <Sfx at={ifAt - 6} name="impact" volume={0.25} />
      {qs.map(q => <Sfx key={q.cue} at={b.cue(q.cue) - 6} name="pop-high" volume={0.18} />)}
      <Sfx at={noAt} name="blip-low" volume={0.3} />
      <Sfx at={stopAt} name="impact" volume={0.2} />
      <Sfx at={skipAt} name="whoosh-short" volume={0.25} />
      <Sfx at={pathsAt} name="chime" volume={0.2} />
    </AbsoluteFill>
  );
};

/** The options of a command, and what «only in these applications» and «block the hotkey» mean. */
export const OptionsPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const opts = [
    { key: 'block', label: t('Block the hotkey'), hint: t('The active application does not receive it.'), toggle: 1 },
    { key: 'remote', label: t('Remote control'), hint: t('Can be run from another device over UDP.'), toggle: 0 },
    { key: 'always', label: t('Always run'), hint: t('Also in the “Disable in” applications.'), toggle: 0 },
    { key: 'sound', label: t('Sound when it runs'), hint: t('Chimes'), toggle: -1 },
    { key: 'apps', label: t('Only in these applications'), hint: t('Photoshop.exe'), toggle: -1 },
    { key: 'notes', label: t('Notes'), hint: t('Opens my workspace for the morning.'), toggle: -1 },
    { key: 'phrases', label: t('Voice phrases'), hint: t('“time to work” · “office mode”'), toggle: -1 },
  ];
  const litKey = (['apps', 'block', 'sound', 'notes', 'phrases'] as const).reduce((s, k) => (f >= b.cue(k) - 6 ? k : s), '' as string);
  const appsShow = inOut(f, b.cue('apps') - 4, b.cue('block') - 8, 10);
  const blockShow = inOut(f, b.cue('block') - 6, b.cue('sound') - 4, 10);
  const inPs = f > b.cue('one') + 6;
  const wins = pop(f, b.cue('wins') - 2);
  const keyDown = inOut(f, b.cue('block') + 8, b.cue('block') + 30, 4);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 110, top: 150, width: 800, padding: 26, borderRadius: 24, background: 'rgba(32,32,38,0.96)', border: `1px solid ${C.line2}`, boxShadow: '0 24px 60px rgba(0,0,0,0.5)', transform: `translateY(${(1 - glide(f, b.from - 10, 24)) * 60}px)` }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 14 }}>
          <KdIcon icon="flash_24_filled" color="gold" size={44} />
          <span style={{ fontFamily: SANS, fontSize: 28, fontWeight: 800, color: C.text }}>{t('Work mode')}</span>
          <span style={{ fontFamily: SANS, fontSize: 18, color: C.dim, marginLeft: 8 }}>{t('Options')}</span>
        </div>
        {opts.map(o => {
          const lit = o.key === litKey;
          return (
            <div key={o.key} style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '13px 16px', borderRadius: 12, marginBottom: 6, background: lit ? 'rgba(242,193,74,0.14)' : 'transparent', border: `1px solid ${lit ? 'rgba(242,193,74,0.6)' : 'transparent'}` }}>
              <div style={{ flex: 1 }}>
                <div style={{ fontFamily: SANS, fontSize: 21, fontWeight: 600, color: C.text }}>{o.label}</div>
                <div style={{ fontFamily: SANS, fontSize: 16, color: lit ? C.accentText : C.dim }}>{o.hint}</div>
              </div>
              {o.toggle >= 0 && <Toggle on={o.toggle} scale={1.2} />}
            </div>
          );
        })}
      </div>

      {/* Same hotkey, two commands: inside Photoshop the specific one wins */}
      <div style={{ position: 'absolute', left: 1000, top: 210, width: 820, opacity: appsShow, transform: `translateX(${(1 - appsShow) * 60}px)` }}>
        {[
          { name: t('Work mode'), scope: 'Every application', icon: 'flash_24_filled', color: 'gold', win: !inPs },
          { name: t('Export for web'), scope: 'Only in Photoshop.exe', icon: 'image_24_filled', color: 'blue', win: inPs },
        ].map((c, i) => (
          <div key={i} style={{
            display: 'flex', alignItems: 'center', gap: 16, padding: '18px 22px', marginBottom: 18, borderRadius: 18, position: 'relative',
            background: c.win ? 'rgba(242,193,74,0.14)' : 'rgba(34,34,40,0.96)', border: `1.5px solid ${c.win ? 'rgba(242,193,74,0.75)' : C.line2}`,
            opacity: pop(f, b.cue(i ? 'one' : 'every') - 6),
          }}>
            <KdIcon icon={c.icon} color={c.color} size={56} />
            <div>
              <div style={{ fontFamily: SANS, fontSize: 25, fontWeight: 700, color: C.text }}>{c.name}</div>
              <div style={{ fontFamily: SANS, fontSize: 18, color: C.dim }}>{c.scope}</div>
            </div>
            <div style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 18, fontWeight: 700, color: C.accentText, background: C.accentSoft, padding: '6px 12px', borderRadius: 10 }}>{t('Ctrl + Alt + W')}</div>
            {i === 1 && <div style={{ position: 'absolute', right: -20, top: -30, transform: `scale(${wins}) rotate(14deg)` }}><Fluent name="crown_24_filled" size={58} color={C.gold1} /></div>}
          </div>
        ))}
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginTop: 10, fontFamily: SANS, fontSize: 22, color: C.text }}>
          <span style={{ color: C.dim }}>{t('In front:')}</span>
          <span style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 16px', borderRadius: 12, background: 'rgba(255,255,255,0.08)' }}>
            <KdIcon icon={inPs ? 'image_24_filled' : 'globe_24_filled'} color={inPs ? 'blue' : 'sky'} size={30} /> {inPs ? t('Photoshop') : t('Browser')}
          </span>
        </div>
      </div>

      {/* Block the hotkey: the app never sees it */}
      <div style={{ position: 'absolute', left: 1040, top: 260, width: 760, height: 420, opacity: blockShow, transform: `translateX(${(1 - blockShow) * 60}px)` }}>
        <div style={{ position: 'absolute', left: 0, top: 120, display: 'flex', gap: 10 }}>
          <Keycap label={t('Ctrl')} height={74} width={96} size={22} down={keyDown} />
          <Keycap label={t('Alt')} height={74} width={96} size={22} down={keyDown} />
          <Keycap label="W" height={74} size={22} down={keyDown} />
        </div>
        <div style={{ position: 'absolute', left: 330, top: 110, transform: `scale(${pop(f, b.cue('block') + 10)})` }}>
          <KdIcon icon="shield_checkmark_24_filled" color="gold" size={96} glow={0.6} />
        </div>
        <div style={{ position: 'absolute', left: 500, top: 60, opacity: 0.9 }}>
          <WinFrame width={260} height={200} title={t('Browser')} style={{ position: 'relative' }}>
            <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <Fluent name="dismiss_circle_24_filled" size={64} color={C.danger} style={{ opacity: ease(f, b.cue('block') + 14, b.cue('block') + 22) }} />
            </div>
          </WinFrame>
        </div>
        <div style={{ position: 'absolute', left: 0, top: 260, fontFamily: SANS, fontSize: 24, color: C.dim, width: 760 }}>{t('Ctrl + W would close the tab. Blocked, only K-Desktop gets it.')}</div>
      </div>
      <Sfx at={b.cue('wins') - 2} name="chime" volume={0.22} />
      <Sfx at={b.cue('block') + 8} name="key" volume={0.4} />
      <Sfx at={b.cue('block') + 12} name="impact" volume={0.15} />
    </AbsoluteFill>
  );
};

/** Elevated K-Desktop, and each action choosing how its program starts. */
export const AdminPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const adminAt = b.cue('admin');
  const elevAt = b.cue('elevated');
  const eachAt = b.cue('each');
  const asAdmin = b.cue('asadmin');
  const asUser = b.cue('user');
  const minimise = ease(f, elevAt + 24, elevAt + 36, OUT);
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 190, transform: `translate(-50%, -50%) scale(${pop(f, adminAt - 8, { damping: 12 })})`, display: 'flex', alignItems: 'center', gap: 20, padding: '18px 34px', borderRadius: 50, background: 'rgba(34,34,40,0.96)', border: `1.5px solid rgba(242,193,74,0.6)` }}>
        <Img src={staticFile('brand/icon-192.png')} style={{ width: 64, height: 64 }} />
        <span style={{ fontFamily: SANS, fontSize: 38, fontWeight: 800, color: C.text }}>{t('K-Desktop')}</span>
        <KdIcon icon="shield_checkmark_24_filled" color="gold" size={52} />
        <span style={{ fontFamily: SANS, fontSize: 30, fontWeight: 700, color: C.accentText }}>{t('Administrator')}</span>
      </div>
      {/* An elevated window still obeys */}
      <div style={{ position: 'absolute', left: 160, top: 330, opacity: inOut(f, elevAt - 8, eachAt, 10) }}>
        <div style={{ transform: `translateY(${minimise * 360}px) scale(${1 - 0.8 * minimise})`, transformOrigin: '50% 100%', opacity: 1 - minimise * 0.9 }}>
          <WinFrame width={620} height={380} title={t('Task Manager (Administrator)')} icon={<KdIcon icon="shield_24_filled" color="blue" size={20} />} style={{ position: 'relative' }}>
            <div style={{ padding: 20, display: 'flex', flexDirection: 'column', gap: 12 }}>
              {Array.from({ length: 7 }, (_, i) => <div key={i} style={{ display: 'flex', gap: 12 }}><div style={{ width: 220, height: 12, borderRadius: 6, background: 'rgba(255,255,255,0.15)' }} /><div style={{ width: 80 + 60 * rand(i), height: 12, borderRadius: 6, background: 'rgba(90,169,255,0.4)' }} /></div>)}
            </div>
          </WinFrame>
        </div>
        <div style={{ position: 'absolute', left: 660, top: 120, display: 'flex', gap: 8, opacity: ease(f, elevAt + 6, elevAt + 14) }}>
          <Keycap label={t('Win')} height={64} width={80} size={20} down={inOut(f, elevAt + 16, elevAt + 30, 3)} />
          <Keycap label="↓" height={64} size={24} down={inOut(f, elevAt + 18, elevAt + 30, 3)} />
        </div>
      </div>
      {/* Each action decides */}
      <div style={{ position: 'absolute', left: 960, top: 600, transform: 'translate(-50%, -50%)', display: 'flex', flexDirection: 'column', gap: 22, opacity: ease(f, eachAt - 6, eachAt + 10) }}>
        {[
          { app: 'regedit.exe', who: 'admin', at: asAdmin },
          { app: 'notepad.exe', who: 'user', at: asUser },
        ].map(r => {
          const sel = f >= r.at - 4;
          return (
            <div key={r.app} style={{ display: 'flex', alignItems: 'center', gap: 18, width: 980, padding: '18px 24px', borderRadius: 18, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${sel ? 'rgba(242,193,74,0.7)' : C.line2}` }}>
              <KdIcon icon="open_24_filled" color="blue" size={54} />
              <div>
                <div style={{ fontFamily: SANS, fontSize: 25, fontWeight: 700, color: C.text }}>{t('Run application')}</div>
                <Mono size={19} color={C.dim}>{r.app}</Mono>
              </div>
              <div style={{ marginLeft: 'auto', display: 'flex', gap: 6, padding: 5, borderRadius: 14, background: 'rgba(255,255,255,0.06)' }}>
                {['user', 'admin'].map(w => {
                  const on = sel && w === r.who;
                  return (
                    <span key={w} style={{ display: 'flex', alignItems: 'center', gap: 8, fontFamily: SANS, fontSize: 19, fontWeight: 700, padding: '8px 16px', borderRadius: 10, background: on ? C.accentText : 'transparent', color: on ? '#1a1306' : C.dim }}>
                      <Fluent name={w === 'admin' ? 'shield_24_filled' : 'person_24_filled'} size={20} color={on ? '#1a1306' : C.dim} />
                      {w === 'admin' ? t('Administrator') : t('Normal user')}
                    </span>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
      <Sfx at={adminAt - 8} name="impact" volume={0.22} />
      <Sfx at={elevAt + 16} name="key" volume={0.4} />
      <Sfx at={elevAt + 26} name="whoosh-down" volume={0.2} />
      <Sfx at={asAdmin - 4} name="blip" volume={0.2} />
      <Sfx at={asUser - 4} name="blip" volume={0.2} />
    </AbsoluteFill>
  );
};

const ICON_SET = [
  'flash_24_filled', 'globe_24_filled', 'folder_24_filled', 'music_note_2_24_filled', 'document_text_24_filled', 'mic_24_filled', 'xbox_controller_24_filled', 'screenshot_24_filled',
  'speaker_2_24_filled', 'power_24_filled', 'lightbulb_filament_24_filled', 'rocket_24_filled', 'heart_24_filled', 'star_24_filled', 'camera_24_filled', 'mail_24_filled',
  'calendar_ltr_24_filled', 'cloud_24_filled', 'games_24_filled', 'home_24_filled', 'video_clip_24_filled', 'code_24_filled', 'broom_24_filled', 'weather_moon_24_filled',
];

/** Icons: chosen automatically, or any symbol in fifteen colours, Windows icons, images, favicons. */
export const IconsPart: React.FC<{ b: BeatTimes }> = ({ b }) => {
  const f = useCurrentFrame();
  const colours = Object.keys(PALETTE);
  const grid = ease(f, b.cue('symbols') - 6, b.cue('symbols') + 10);
  const wave = (f - b.cue('colors')) / 4;
  const sources = [
    { cue: 'winicons', title: t('Windows icons'), sub: t('shell32 · imageres'), icon: 'window_24_filled', color: 'blue' },
    { cue: 'images', title: t('Your images'), sub: t('.png · .jpg · .ico'), icon: 'image_24_filled', color: 'green' },
    { cue: 'favicon', title: t('Website logo'), sub: t('its own favicon'), icon: 'globe_24_filled', color: 'sky' },
  ];
  const autoRows = [
    { action: 'Run application · Spotify.exe', icon: 'music_note_2_24_filled', color: 'green' },
    { action: 'Open folder · Downloads', icon: 'arrow_download_24_filled', color: 'teal' },
    { action: 'Power · Shut down', icon: 'power_24_filled', color: 'red' },
  ];
  const autoShow = inOut(f, b.cue('auto') - 10, b.cue('symbols') - 4, 10);
  return (
    <AbsoluteFill>
      <Camera shots={[{ at: 0, x: 960, y: 540, s: 1.05 }, { at: b.cue('symbols'), dur: 60, x: 960, y: 520, s: 1 }, { at: b.cue('winicons') - 10, dur: 40, x: 960, y: 560, s: 0.96, rx: 4 }]}>
        <div style={{ position: 'absolute', left: 960, top: 520, transform: 'translate(-50%, -50%)', display: 'flex', flexDirection: 'column', gap: 18, opacity: autoShow }}>
          {autoRows.map((r, i) => {
            const k = ease(f, b.cue('auto') + i * 6, b.cue('auto') + i * 6 + 16, OUT);
            return (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 22, width: 900, padding: '16px 24px', borderRadius: 18, background: 'rgba(34,34,40,0.96)', border: `1px solid ${C.line2}` }}>
                <div style={{ width: 64, height: 64, position: 'relative' }}>
                  <div style={{ position: 'absolute', inset: 0, borderRadius: 14, border: `2px dashed ${C.faint}`, opacity: 1 - k }} />
                  <div style={{ position: 'absolute', inset: 0, transform: `scale(${k})` }}><KdIcon icon={r.icon} color={r.color} size={64} /></div>
                </div>
                <Mono size={24} color={C.text}>{r.action}</Mono>
                <span style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 18, color: C.accentText, opacity: k }}>{t('automatic')}</span>
              </div>
            );
          })}
        </div>
        <div style={{ position: 'absolute', left: 960, top: 420, transform: `translate(-50%, -50%) scale(${0.9 + 0.1 * grid})`, opacity: grid, display: 'grid', gridTemplateColumns: 'repeat(12, 92px)', gap: 18 }}>
          {Array.from({ length: 36 }, (_, i) => {
            const col = i % 12;
            const row = Math.floor(i / 12);
            const idx = f > b.cue('colors') - 4 ? Math.floor(Math.max(0, wave - col - row)) % colours.length : (i * 5) % colours.length;
            const k = pop(f, b.cue('symbols') - 6 + (col + row) * 1.2, { damping: 14 });
            return <KdIcon key={i} icon={ICON_SET[i % ICON_SET.length]} color={colours[(idx + i) % colours.length]} size={92 * k} />;
          })}
        </div>
        <div style={{ position: 'absolute', left: 960, top: 880, transform: 'translate(-50%, -50%)', display: 'flex', gap: 30 }}>
          {sources.map(s => {
            const k = pop(f, b.cue(s.cue) - 6, { damping: 13 });
            return (
              <div key={s.cue} style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '16px 26px', borderRadius: 20, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 2), transform: `translateY(${(1 - k) * 60}px)` }}>
                <KdIcon icon={s.icon} color={s.color} size={56} />
                <div>
                  <div style={{ fontFamily: SANS, fontSize: 25, fontWeight: 700, color: C.text }}>{s.title}</div>
                  <div style={{ fontFamily: SANS, fontSize: 17, color: C.dim }}>{s.sub}</div>
                </div>
              </div>
            );
          })}
        </div>
      </Camera>
      {[0, 6, 12].map(d => <Sfx key={d} at={b.cue('auto') + d} name="pop" volume={0.2} />)}
      <Sfx at={b.cue('symbols') - 6} name="rise" volume={0.2} />
      {sources.map(s => <Sfx key={s.cue} at={b.cue(s.cue) - 6} name="pop-low" volume={0.22} />)}
    </AbsoluteFill>
  );
};
