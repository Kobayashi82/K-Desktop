import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot, Focus } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { Fluent } from '../../../src/components/Icon';
import { Mono } from '../../../src/components/Keys';
import { Sfx } from '../../../src/components/Sfx';
import { ease, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { Terminal } from './Parts';

/** --assistant-dev, and the loop: build, and it is reloaded. */
const Dev: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const d = B('dev');
  const at = d.cue('reload') - 6;
  const steps: Array<[string, string]> = [['wrench_24_filled', 'dotnet build'], ['arrow_sync_24_filled', ''], ['checkmark_circle_24_filled', t('Reloaded on the desktop')]];
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <Terminal width={1300} height={200} style={{ left: 310, top: 220 }} lines={[
        { at: d.cue('dev') - 8, cmd: 'KDesktop.exe --assistant-dev "C:\\Code\\MyAssistant"' },
      ]} />
      <div style={{ position: 'absolute', left: 0, right: 0, top: 540, display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 34 }}>
        {steps.map(([icon, label], i) => {
          const k = pop(f, at + i * 8, { damping: 14 });
          const spin = i === 1 ? (f - at) * 6 : 0;
          return (
            <div key={i} style={{
              display: 'flex', alignItems: 'center', gap: 14, padding: label ? '18px 30px' : 18, borderRadius: 40, background: 'rgba(40,40,48,0.96)', border: `1.5px solid ${C.line2}`,
              opacity: Math.min(1, k * 1.4), transform: `scale(${0.8 + 0.2 * k})`,
            }}>
              <Fluent name={icon} size={40} color={i === 2 ? C.success : C.accentText} style={{ transform: `rotate(${spin}deg)` }} />
              {label && (i === 0 ? <Mono size={30}>{label}</Mono> : <span style={{ fontSize: 30, fontWeight: 700, color: C.text }}>{label}</span>)}
            </div>
          );
        })}
      </div>
      <Sfx at={at + 16} name="chime" volume={0.18} />
    </AbsoluteFill>
  );
};

/** The Lander's own tabs (lander-settings.png): General, Look, Behaviour, Flying and Pilot come from its settings.json. */
const Tabs: React.FC = () => {
  const B = useBeats();
  const s = B('settings');
  const focus: Focus[] = [{ from: s.cue('settings') - 4, to: s.cue('panel') - 4, rect: [562, 256, 458, 30], label: t('From its settings.json'), at: 'bottom' }];
  return (
    <>
      <CaptureShot name="lander-settings" focus={focus}
        shots={[{ at: s.from - 10, x: 960, y: 540, s: 1.0 }, { at: s.cue('settings') - 14, dur: 26, x: 900, y: 300, s: 1.7 }]} />
      <Sfx at={s.cue('settings') - 4} name="blip" volume={0.12} />
    </>
  );
};

// What each file may hold (site/en/sdk/getting-started.md, «Settings and panel»).
const FILES: Array<[string, string, string[]]> = [
  ['settings', 'settings.json', ['toggle', 'slider', 'number', 'choice', 'text', 'color', 'gallery', 'apps', 'hotkey', 'button']],
  ['panel', 'panel.json', ['stat', 'progress', 'list', 'achievements', 'buttons', 'text']],
];

/** settings.json and panel.json, drawn by K-Desktop without loading the assistant's code. */
const Files: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const s = B('settings');
  const badge = ease(f, s.cue('nocode') - 4, s.cue('nocode') + 8, OUT);
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      {FILES.map(([cue, name, items], row) => {
        const k = pop(f, (row === 0 ? s.cue('panel') - 20 : s.cue('panel')) - 4, { damping: 15 });
        return (
          <div key={cue} style={{
            position: 'absolute', left: 260, top: 180 + row * 250, width: 1400, padding: '24px 30px', borderRadius: 22, boxSizing: 'border-box',
            background: 'rgba(30,30,36,0.96)', border: `1.5px solid ${C.line2}`, opacity: Math.min(1, k * 1.4), transform: `translateY(${(1 - k) * 40}px)`,
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
              <Fluent name="document_24_regular" size={34} color={C.accentText} />
              <Mono size={32}>{name}</Mono>
              <span style={{ fontSize: 24, color: C.dim, marginLeft: 12 }}>{row === 0 ? t('→ its tabs in Settings') : t('→ its own tab in the Assistant panel')}</span>
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginTop: 18 }}>
              {items.map(i => <div key={i} style={{ padding: '8px 18px', borderRadius: 20, background: 'rgba(255,255,255,0.07)' }}><Mono size={22} color={C.text}>{i}</Mono></div>)}
            </div>
          </div>
        );
      })}
      <div style={{
        position: 'absolute', left: 0, right: 0, top: 720, display: 'flex', justifyContent: 'center', opacity: badge, transform: `scale(${0.9 + 0.1 * badge})`,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: '14px 30px', borderRadius: 30, background: 'linear-gradient(180deg, #FFD970, #F2B807)', color: '#1a1306', fontSize: 30, fontWeight: 800 }}>
          <Fluent name="shield_checkmark_24_filled" size={34} color="#1a1306" /> {t('Drawn by K-Desktop, without loading your code')}
        </div>
      </div>
      <Sfx at={s.cue('panel') - 4} name="pop" volume={0.18} />
      <Sfx at={s.cue('nocode') - 4} name="chime" volume={0.18} />
    </AbsoluteFill>
  );
};

/** Chapter «Try it». */
export const Try: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const s = B('settings');
  return (
    <AbsoluteFill>
      <Part from={0} to={s.from - 6} first><Dev /></Part>
      <Part from={s.from - 6} to={s.cue('panel') - 24}><Tabs /></Part>
      <Part from={s.cue('panel') - 24} to={durationInFrames} last><Files /></Part>
    </AbsoluteFill>
  );
};
