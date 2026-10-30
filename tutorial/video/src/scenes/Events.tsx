import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../components/Chapter';
import { AppWindow, Taskbar, Wallpaper } from '../components/Desktop';
import { Kinetic } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Mono } from '../components/Keys';
import { Osd } from '../components/Osd';
import { Sfx } from '../components/Sfx';
import { ease, glide, inOut, OUT, pop } from '../lib/anim';
import { BeatTimes, useBeats, useChapter } from '../lib/timeline';
import { C, SANS } from '../theme';
import { t } from '../lib/i18n';

/**
 * The kinds (EventCatalog.cs), in its order, with the cue that names each. The long video tells the first 16; a script
 * with a «kinds3» beat (the short video) also gets the six added later.
 */
const KINDS: Array<{ name: string; icon: string; color: string; beat: 'kinds' | 'kinds2' | 'kinds3'; cue: string }> = [
  { name: t('When K-Desktop starts'), icon: 'play_24_filled', color: 'green', beat: 'kinds', cue: 'start' },
  { name: t('When K-Desktop exits'), icon: 'power_24_filled', color: 'red', beat: 'kinds', cue: 'start' },
  { name: t('At a time'), icon: 'clock_24_filled', color: 'blue', beat: 'kinds', cue: 'time' },
  { name: t('Every so often'), icon: 'arrow_repeat_all_24_filled', color: 'sky', beat: 'kinds', cue: 'interval' },
  { name: t('When a program opens'), icon: 'window_apps_24_filled', color: 'indigo', beat: 'kinds', cue: 'program' },
  { name: t('Program in front'), icon: 'window_arrow_up_24_filled', color: 'purple', beat: 'kinds', cue: 'front' },
  { name: t('PC not in use'), icon: 'weather_moon_24_filled', color: 'dark', beat: 'kinds', cue: 'idle' },
  { name: t('Lock, sleep…'), icon: 'lock_closed_24_filled', color: 'orange', beat: 'kinds', cue: 'lock' },
  { name: t('Battery or mains'), icon: 'battery_charge_24_filled', color: 'lime', beat: 'kinds', cue: 'battery' },
  { name: t('Device connected'), icon: 'xbox_controller_24_filled', color: 'green', beat: 'kinds2', cue: 'controller' },
  { name: t('Audio device'), icon: 'headphones_sound_wave_24_filled', color: 'teal', beat: 'kinds2', cue: 'headphones' },
  { name: t('Drive connected'), icon: 'usb_stick_24_filled', color: 'gray', beat: 'kinds2', cue: 'usb' },
  { name: t('Network'), icon: 'wifi_1_24_filled', color: 'teal', beat: 'kinds2', cue: 'network' },
  { name: t('New file in a folder'), icon: 'document_add_24_filled', color: 'yellow', beat: 'kinds2', cue: 'file' },
  { name: t('PC usage'), icon: 'data_usage_24_filled', color: 'orange', beat: 'kinds2', cue: 'cpu' },
  { name: t('Call from outside'), icon: 'plug_connected_24_filled', color: 'pink', beat: 'kinds2', cue: 'remote' },
  { name: t('Battery level'), icon: 'battery_warning_24_filled', color: 'lime', beat: 'kinds3', cue: 'level' },
  { name: t('Sunrise or sunset'), icon: 'weather_sunny_24_filled', color: 'orange', beat: 'kinds3', cue: 'sun' },
  { name: t('Screen plugged in or out'), icon: 'desktop_24_filled', color: 'blue', beat: 'kinds3', cue: 'screen' },
  { name: t('Calendar event'), icon: 'calendar_ltr_24_filled', color: 'orange', beat: 'kinds3', cue: 'calendar' },
  { name: t('Mail arrives'), icon: 'mail_24_filled', color: 'blue', beat: 'kinds3', cue: 'mail' },
  { name: t('A Telegram message arrives'), icon: 'send_24_filled', color: 'sky', beat: 'kinds3', cue: 'telegram' },
];
const FIRST16 = KINDS.filter(k => k.beat !== 'kinds3');

const Rule: React.FC<{ intro: BeatTimes }> = ({ intro }) => {
  const f = useCurrentFrame();
  const at = intro.cue('when');
  const slot = Math.floor(Math.max(0, f - at) / 12) % FIRST16.length;
  const k = pop(f, at - 4, { damping: 13 });
  return (
    <AbsoluteFill>
      <Kinetic text={t('Your PC triggers them for you')} at={intro.cue('events') - 4} x={960} y={300} size={60} color={C.dim} weight={700} />
      <div style={{ position: 'absolute', left: 960, top: 600, transform: `translate(-50%, -50%) scale(${0.8 + 0.2 * k})`, opacity: k, display: 'flex', alignItems: 'center', gap: 28 }}>
        <span style={{ fontFamily: SANS, fontSize: 64, fontWeight: 800, color: C.accentText }}>{t('WHEN')}</span>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '18px 28px', borderRadius: 24, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${C.line2}`, minWidth: 520 }}>
          <KdIcon icon={FIRST16[slot].icon} color={FIRST16[slot].color} size={66} />
          <span style={{ fontFamily: SANS, fontSize: 34, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{FIRST16[slot].name}</span>
        </div>
        <Fluent name="arrow_right_24_filled" size={60} color={C.accentText} />
        <span style={{ fontFamily: SANS, fontSize: 64, fontWeight: 800, color: C.accentText }}>{t('RUN')}</span>
        <KdIcon icon="flash_24_filled" color="gold" size={86} glow={0.5} />
      </div>
      <Sfx at={at - 4} name="pop" volume={0.3} />
    </AbsoluteFill>
  );
};

const Ring: React.FC<{ kinds: BeatTimes; kinds2: BeatTimes; kinds3?: BeatTimes }> = ({ kinds, kinds2, kinds3 }) => {
  const f = useCurrentFrame();
  const list = kinds3 ? KINDS : FIRST16;
  const beats = { kinds, kinds2, kinds3: kinds3 ?? kinds2 };
  const times = list.map((k, i) => beats[k.beat].cue(k.cue) + (i === 1 ? 6 : 0));
  const current = times.reduce((c, t, i) => (f >= t - 4 ? i : c), -1);
  const spin = (f - kinds.from) * 0.05;
  const counter = Math.round(list.length * ease(f, kinds.from, kinds.cue('sixteen') + 16, OUT));
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 960, top: 480, transform: 'translate(-50%, -50%)', textAlign: 'center' }}>
        <div style={{ fontFamily: SANS, fontSize: 140, fontWeight: 800, color: C.accentText, lineHeight: 1 }}>{counter}</div>
        <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 700, color: C.dim, letterSpacing: 4 }}>{t('TRIGGERS')}</div>
        <div style={{ marginTop: 26, height: 44, fontFamily: SANS, fontSize: 32, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{current >= 0 ? list[current].name : ''}</div>
      </div>
      {list.map((k, i) => {
        const angle = (i / list.length) * Math.PI * 2 - Math.PI / 2 + spin * 0.02;
        const x = 960 + Math.cos(angle) * 640;
        const y = 480 + Math.sin(angle) * 340;
        const s = pop(f, times[i] - 6, { damping: 12, stiffness: 160 });
        const lit = i === current ? 1 : 0;
        return (
          <div key={k.name} style={{ position: 'absolute', left: x, top: y, transform: `translate(-50%, -50%) scale(${s * (1 + 0.25 * lit)})`, opacity: Math.min(1, s * 2) * (current >= 0 && !lit ? 0.75 : 1) }}>
            <KdIcon icon={k.icon} color={k.color} size={96} glow={lit} />
          </div>
        );
      })}
      {list.map((k, i) => <Sfx key={i} at={times[i] - 6} name="pop-high" volume={0.14} />)}
    </AbsoluteFill>
  );
};

/** «Gaming audio»: when Steam opens, headphones; when it ends, speakers. */
const Example: React.FC<{ example: BeatTimes; ends: BeatTimes; safety: BeatTimes }> = ({ example, ends, safety }) => {
  const f = useCurrentFrame();
  const opensAt = example.cue('when') + 4;
  const switchAt = example.cue('switch') + 6;
  const closesAt = ends.cue('closes') + 4;
  const backAt = ends.cue('back') + 6;
  const steam = pop(f, opensAt, { damping: 15 }) * (1 - ease(f, closesAt, closesAt + 10));
  const endsShow = glide(f, ends.cue('ends') - 6, 18);
  const fire1 = inOut(f, switchAt - 6, switchAt + 30, 6);
  const fire2 = inOut(f, backAt - 6, backAt + 30, 6);
  const toSafety = glide(f, safety.from - 8, 26);
  const chips = [
    { cue: 'conditions', icon: 'filter_24_filled', color: 'orange', title: t('Conditions'), sub: t('Only if it is after 18:00') },
    { cue: 'cooldown', icon: 'timer_off_24_filled', color: 'purple', title: t('Minimum time between runs'), sub: t('At most once every 5 min') },
    { cue: 'loops', icon: 'arrow_sync_24_filled', color: 'red', title: t('Loop protection'), sub: t('It can never keep setting itself off') },
  ];
  return (
    <AbsoluteFill>
      {/* A small desktop where it happens */}
      <div style={{ position: 'absolute', left: 90, top: 200, width: 860, height: 520, borderRadius: 20, overflow: 'hidden', border: `2px solid ${C.line2}`, transform: `translateX(${-toSafety * 1100}px)`, boxShadow: '0 30px 70px rgba(0,0,0,0.5)' }}>
        <div style={{ position: 'absolute', left: 0, top: 0, width: 1920, height: 1080, transform: 'scale(0.448)', transformOrigin: '0 0' }}>
          <Wallpaper hue={40} />
          <div style={{ position: 'absolute', left: 300, top: 120, transform: `scale(${steam})`, opacity: Math.min(1, steam * 2) }}>
            <AppWindow kind="steam" width={1300} height={760} style={{ position: 'relative' }} />
          </div>
          <Taskbar apps={[{ icon: 'folder_24_filled', color: 'yellow' }, { icon: 'xbox_controller_24_filled', color: 'indigo', active: steam > 0.5 }]} />
          <div style={{ position: 'absolute', left: 960, top: 860, transform: 'translateX(-50%) scale(1.8)', transformOrigin: 'top center' }}>
            <Osd icon={f < backAt - 6 ? 'headphones_sound_wave_24_filled' : 'speaker_2_24_filled'} color="purple" text={`${t('Audio output')}: ${t(f < backAt - 6 ? 'Headphones' : 'Speakers')}`} show={Math.max(fire1, fire2)} />
          </div>
        </div>
      </div>
      {/* The event */}
      <div style={{
        position: 'absolute', left: 1010, top: 170, width: 820, padding: 28, borderRadius: 26, background: 'rgba(32,32,38,0.97)', border: `1.5px solid rgba(242,193,74,${0.25 + 0.6 * Math.max(fire1, fire2)})`,
        boxShadow: `0 30px 70px rgba(0,0,0,0.5), 0 0 ${60 * Math.max(fire1, fire2)}px rgba(242,193,74,0.3)`, transform: `translateX(${(1 - glide(f, example.from - 10, 24)) * 80 - toSafety * 900}px) scale(${1 - 0.1 * toSafety})`,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 22 }}>
          <KdIcon icon="window_apps_24_filled" color="indigo" size={58} />
          <div>
            <div style={{ fontFamily: SANS, fontSize: 15, fontWeight: 700, letterSpacing: 2, color: C.accentText }}>{t('EVENT')}</div>
            <div style={{ fontFamily: SANS, fontSize: 32, fontWeight: 800, color: C.text }}>{t('Gaming audio')}</div>
          </div>
        </div>
        <div style={{ fontFamily: SANS, fontSize: 18, fontWeight: 700, color: C.dim, marginBottom: 10 }}>{t('WHEN')}</div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '14px 18px', borderRadius: 14, background: 'rgba(255,255,255,0.05)', marginBottom: 20 }}>
          <span style={{ fontFamily: SANS, fontSize: 22, fontWeight: 600, color: C.text }}>{t('When a program opens')}</span>
          <span style={{ marginLeft: 'auto' }}><Mono size={20} color={C.accentText}>{t('steam.exe')}</Mono></span>
        </div>
        <div style={{ fontFamily: SANS, fontSize: 18, fontWeight: 700, color: C.dim, marginBottom: 10 }}>{t('RUN')}</div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '12px 18px', borderRadius: 14, background: fire1 > 0.1 ? 'rgba(242,193,74,0.14)' : 'rgba(255,255,255,0.05)', border: `1px solid ${fire1 > 0.1 ? 'rgba(242,193,74,0.6)' : 'transparent'}`, marginBottom: 20 }}>
          <KdIcon icon="headphones_sound_wave_24_filled" color="purple" size={44} />
          <span style={{ fontFamily: SANS, fontSize: 22, fontWeight: 600, color: C.text }}>{t('Headphones')}</span>
          <span style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 17, color: C.dim }}>{t('Audio output')}</span>
        </div>
        <div style={{ height: 150 * endsShow, overflow: 'hidden', opacity: endsShow }}>
          <div style={{ fontFamily: SANS, fontSize: 18, fontWeight: 700, color: C.accentText, marginBottom: 10 }}>{t('WHEN IT ENDS')}</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '12px 18px', borderRadius: 14, background: fire2 > 0.1 ? 'rgba(242,193,74,0.14)' : 'rgba(255,255,255,0.05)', border: `1px solid ${fire2 > 0.1 ? 'rgba(242,193,74,0.6)' : 'transparent'}` }}>
            <KdIcon icon="speaker_2_24_filled" color="blue" size={44} />
            <span style={{ fontFamily: SANS, fontSize: 22, fontWeight: 600, color: C.text }}>{t('Speakers')}</span>
            <span style={{ marginLeft: 'auto', fontFamily: SANS, fontSize: 17, color: C.dim }}>{t('Audio output')}</span>
          </div>
        </div>
      </div>
      {/* Safety */}
      <div style={{ position: 'absolute', left: 1080, top: 250, display: 'flex', flexDirection: 'column', gap: 26 }}>
        {chips.map(c => {
          const k = pop(f, safety.cue(c.cue) - 6, { damping: 13 });
          return (
            <div key={c.cue} style={{ display: 'flex', alignItems: 'center', gap: 20, width: 700, padding: '22px 26px', borderRadius: 22, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 2), transform: `translateX(${(1 - k) * 100}px)` }}>
              <KdIcon icon={c.icon} color={c.color} size={64} />
              <div>
                <div style={{ fontFamily: SANS, fontSize: 28, fontWeight: 700, color: C.text }}>{c.title}</div>
                <div style={{ fontFamily: SANS, fontSize: 20, color: C.dim }}>{c.sub}</div>
              </div>
            </div>
          );
        })}
      </div>
      <Sfx at={opensAt} name="pop" volume={0.3} />
      <Sfx at={switchAt - 6} name="chime" volume={0.2} />
      <Sfx at={ends.cue('ends') - 6} name="whoosh-short" volume={0.2} />
      <Sfx at={closesAt} name="whoosh-down" volume={0.2} />
      <Sfx at={backAt - 6} name="chime" volume={0.2} />
      {chips.map(c => <Sfx key={c.cue} at={safety.cue(c.cue) - 6} name="pop-low" volume={0.22} />)}
    </AbsoluteFill>
  );
};

export const Events: React.FC = () => {
  const B = useBeats();
  const { durationInFrames, beats } = useChapter();
  const hasKinds3 = beats.some(b => b.id === 'kinds3');
  const intro = B('intro');
  const kinds = B('kinds');
  const kinds2 = B('kinds2');
  const example = B('example');
  return (
    <AbsoluteFill>
      <Part from={0} to={kinds.from - 4} first><Rule intro={intro} /></Part>
      <Part from={kinds.from - 4} to={example.from - 6}><Ring kinds={kinds} kinds2={kinds2} kinds3={hasKinds3 ? B('kinds3') : undefined} /></Part>
      <Part from={example.from - 6} to={durationInFrames} last><Example example={example} ends={B('ends')} safety={B('safety')} /></Part>
    </AbsoluteFill>
  );
};
