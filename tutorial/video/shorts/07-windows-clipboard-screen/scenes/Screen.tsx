import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Cursor } from '../../../src/components/Cursor';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { WinFrame } from '../../../src/components/KdWindow';
import { Osd } from '../../../src/components/Osd';
import { Sfx } from '../../../src/components/Sfx';
import { Toggle } from '../../../src/components/Ui';
import { ease, glide, inOut, OUT, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { Desk, DESK_W, px, Rect } from './Desk';

const PHOTO: Rect = { x: 0.04, y: 0.05, w: 0.54, h: 0.8 };
const ERROR: Rect = { x: 0.6, y: 0.32, w: 0.36, h: 0.32 };
const HAND = "'Segoe Print', 'Comic Sans MS', cursive";
const LINES = [t('Wi-Fi: Home-5G'), t('Password: moon-lander-82')];

/** A photo of a sticky note on a wall: text you can see but not select. */
const Photo: React.FC<{ show: number }> = ({ show }) => {
  const p = px(PHOTO);
  return (
    <WinFrame width={p.width} height={p.height} title={t('IMG_2041.jpg - Photos')} bar={36} bg="#1f1f23"
      icon={<KdIcon icon="image_24_filled" color="sky" size={18} />}
      style={{ left: p.left, top: p.top, opacity: Math.min(1, show * 1.4), transform: `scale(${0.85 + 0.15 * show})` }}>
      <div style={{ position: 'absolute', inset: 0, background: 'radial-gradient(ellipse at 40% 35%, #8a7a66, #4a3f34 75%)' }}>
        <div style={{
          position: 'absolute', left: 150, top: 150, width: 520, height: 290, transform: 'rotate(-3deg)', background: 'linear-gradient(180deg, #ffe97a, #f5d64f)',
          boxShadow: '0 18px 30px rgba(0,0,0,0.45)', padding: '46px 36px', boxSizing: 'border-box', fontFamily: HAND, fontSize: 30, lineHeight: 1.7, color: '#2b3a8c',
        }}>
          {LINES.map(l => <div key={l} style={{ whiteSpace: 'nowrap' }}>{l}</div>)}
        </div>
      </div>
    </WinFrame>
  );
};

/** An error message whose text can't be selected. */
const ErrorBox: React.FC<{ show: number }> = ({ show }) => {
  const p = px(ERROR);
  return (
    <WinFrame width={p.width} height={p.height} title={t('Setup')} bar={36} bg="#2b2b30"
      style={{ left: p.left, top: p.top, opacity: Math.min(1, show * 1.4), transform: `scale(${0.85 + 0.15 * show})` }}>
      <div style={{ padding: '22px 26px', display: 'flex', gap: 18, fontFamily: SANS, color: C.text }}>
        <Fluent name="error_circle_24_filled" size={44} color="#ff6b6b" />
        <div>
          <div style={{ fontSize: 20, fontWeight: 600 }}>{t('Installation failed')}</div>
          <div style={{ fontSize: 16, color: C.dim, marginTop: 8 }}>{t('Error 0x80070643 in package atlas-tools.msi')}</div>
        </div>
      </div>
      <div style={{ position: 'absolute', right: 22, bottom: 20, padding: '8px 30px', borderRadius: 6, background: '#3a3a42', fontFamily: SANS, fontSize: 15, color: C.text }}>OK</div>
    </WinFrame>
  );
};

/** A gold outline around a place of the stage screen. */
const Outline: React.FC<{ r: { left: number; top: number; width: number; height: number }; show: number; dashed?: boolean }> = ({ r, show, dashed }) => (
  <div style={{
    position: 'absolute', ...r, borderRadius: 10, opacity: show, boxSizing: 'border-box', border: `4px ${dashed ? 'dashed' : 'solid'} ${C.accentText}`,
    background: 'rgba(242,193,74,0.08)', boxShadow: '0 0 30px rgba(242,193,74,0.4)',
  }} />
);

/** Chapter «Text from the screen»: a box around a photo's text, the other areas, the result, and how it reads it. */
export const Screen: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const what = B('what');
  const how = B('how');
  const local = B('local');
  const region = how.cue('region');
  const copied = how.cue('copied');
  // The box around the sticky note, in stage screen pixels.
  const from = { x: 232, y: 256 };
  const to = { x: 752, y: 402 };
  const drag = ease(f, region + 8, region + 34, OUT);
  const box = { left: from.x, top: from.y, width: (to.x - from.x) * drag, height: (to.y - from.y) * drag };
  const boxShow = Math.max(inOut(f, region + 4, how.cue('window') - 2, 6), inOut(f, copied - 8, local.from + 10, 6));
  const scan = ease(f, copied, copied + 22);
  const side = ease(f, local.from - 6, local.from + 22);
  const action = pop(f, what.cue('action') - 8, { damping: 14 });
  const areas = [
    { cue: 'region', label: t('A region') },
    { cue: 'window', label: t('The active window') },
    { cue: 'monitor', label: t("The mouse's monitor") },
  ];
  const current = areas.reduce((s, a, i) => (f >= how.cue(a.cue) - 4 ? i : s), -1);
  const chips = inOut(f, region - 6, copied - 2, 8);
  const osd = inOut(f, copied + 22, local.from + 6, 8);
  const editor = glide(f, local.from + 4, 22);
  return (
    <AbsoluteFill>
      <Desk x={960 - 470 * side} y={520 + 20 * side} s={0.72 - 0.26 * side} apps={['code', 'chrome']}>
        <Photo show={pop(f, what.cue('picture') - 8, { damping: 13 })} />
        <ErrorBox show={pop(f, what.cue('error') - 8, { damping: 13 })} />
        <Outline r={box} show={boxShow} dashed />
        {scan > 0 && scan < 1 && <div style={{ position: 'absolute', left: from.x, width: to.x - from.x, top: from.y + (to.y - from.y) * scan, height: 3, background: '#C3EB5C', boxShadow: '0 0 14px #C3EB5C' }} />}
        <Outline r={px(ERROR, -6)} show={inOut(f, how.cue('window') - 4, how.cue('monitor') - 4, 6)} />
        <Outline r={{ left: 6, top: 6, width: DESK_W - 12, height: 900 - 12 }} show={inOut(f, how.cue('monitor') - 4, copied - 6, 6)} />
        <Cursor path={[
          { at: 0, x: 600, y: 560 },
          { at: region - 16, x: from.x, y: from.y, dur: 20 },
          { at: region + 8, x: to.x, y: to.y, dur: 26 },
          { at: how.cue('window') - 10, x: 1150, y: 420, dur: 20 },
          { at: how.cue('monitor') - 10, x: 1000, y: 650, dur: 20 },
        ]} clicks={[{ at: region + 6 }]} scale={1.3} show={inOut(f, region - 20, copied, 8)} />
      </Desk>
      <div style={{
        position: 'absolute', left: 960 - 470 * side, top: 100, transform: `translate(-50%, -50%) scale(${action})`, opacity: Math.min(1, action * 1.4) * (1 - side),
        display: 'flex', alignItems: 'center', gap: 18, padding: '14px 30px 14px 16px', borderRadius: 22, background: 'rgba(30,30,36,0.94)', border: '1.5px solid rgba(242,193,74,0.5)',
        boxShadow: '0 20px 50px rgba(0,0,0,0.45)',
      }}>
        <KdIcon icon="scan_text_24_filled" color="lime" size={56} />
        <span style={{ fontFamily: SANS, fontSize: 36, fontWeight: 700, color: C.text, whiteSpace: 'nowrap' }}>{t('Copy text from the screen')}</span>
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 940, display: 'flex', justifyContent: 'center', gap: 16, opacity: chips }}>
        {areas.map((a, i) => (
          <div key={a.cue} style={{
            padding: '12px 24px', borderRadius: 14, fontFamily: SANS, fontSize: 26, fontWeight: 600,
            color: i === current ? '#1a1306' : C.dim, background: i === current ? 'linear-gradient(180deg, #FFD970, #F2B807)' : 'rgba(40,40,48,0.94)',
            border: i === current ? undefined : `1.5px solid ${C.line2}`,
          }}>{a.label}</div>
        ))}
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 900, display: 'flex', justifyContent: 'center' }}>
        <Osd icon="scan_text_24_filled" color="lime" text={t('Text copied (2 lines): Wi-Fi: Home-5G…')} show={osd} scale={1.6} />
      </div>
      {/* How it reads it: the action's options and what it doesn't need. */}
      <div style={{
        position: 'absolute', left: 960, top: 210, width: 820, padding: 32, borderRadius: 28, boxSizing: 'border-box', fontFamily: SANS,
        background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
        opacity: Math.min(1, editor * 1.4), transform: `translateX(${(1 - editor) * 80}px)`,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 20, marginBottom: 26 }}>
          <KdIcon icon="scan_text_24_filled" color="lime" size={64} />
          <span style={{ fontSize: 32, fontWeight: 700, color: C.text }}>{t('Copy text from the screen')}</span>
        </div>
        {[
          { label: t('What to capture'), value: t('A region') },
          { label: t('Language of the text'), value: t('Automatic'), cue: 'language' },
        ].map(r => {
          const lit = r.cue ? inOut(f, local.cue(r.cue) - 4, local.to + 40, 8) : 0;
          return (
            <div key={r.label} style={{ marginBottom: 18 }}>
              <div style={{ fontSize: 20, color: C.dim, marginBottom: 8 }}>{r.label}</div>
              <div style={{
                display: 'flex', alignItems: 'center', height: 54, padding: '0 20px', borderRadius: 10, fontSize: 22, color: C.text,
                background: 'rgba(255,255,255,0.06)', border: `1.5px solid rgba(242,193,74,${0.1 + 0.8 * lit})`, boxShadow: lit ? `0 0 ${30 * lit}px rgba(242,193,74,${0.3 * lit})` : undefined,
              }}>
                {r.value}
                <Fluent name="chevron_down_24_regular" size={20} color={C.dim} style={{ marginLeft: 'auto' }} />
              </div>
            </div>
          );
        })}
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, fontSize: 22, color: C.text, marginTop: 6 }}>
          <Toggle on={0} /> {t('All on one line')}
        </div>
        <div style={{ display: 'flex', gap: 14, marginTop: 28 }}>
          {[
            { icon: 'scan_text_24_filled', label: t("Windows' text recognition") },
            { icon: 'lock_closed_24_filled', label: t('No AI · nothing leaves your PC') },
          ].map((b, i) => {
            const k = pop(f, local.cue('windows') - 8 + i * 8, { damping: 14 });
            return (
              <div key={b.label} style={{
                display: 'flex', alignItems: 'center', gap: 10, padding: '10px 18px', borderRadius: 12, fontSize: 20, fontWeight: 600, color: C.accentText,
                background: C.accentSoft, opacity: Math.min(1, k * 1.4), transform: `scale(${0.8 + 0.2 * k})`, whiteSpace: 'nowrap',
              }}>
                <Fluent name={b.icon} size={22} color={C.accentText} /> {b.label}
              </div>
            );
          })}
        </div>
      </div>
      <Sfx at={what.cue('picture') - 8} name="pop" volume={0.2} />
      <Sfx at={what.cue('error') - 8} name="pop-low" volume={0.25} />
      <Sfx at={what.cue('action') - 8} name="pop" volume={0.22} />
      <Sfx at={region + 6} name="click" volume={0.25} />
      <Sfx at={how.cue('window') - 4} name="blip" volume={0.14} />
      <Sfx at={how.cue('monitor') - 4} name="blip" volume={0.14} />
      <Sfx at={copied} name="rise" volume={0.18} />
      <Sfx at={copied + 22} name="chime" volume={0.2} />
      <Sfx at={local.cue('windows') - 8} name="pop-high" volume={0.18} />
      <Sfx at={local.cue('language') - 4} name="blip" volume={0.14} />
    </AbsoluteFill>
  );
};
