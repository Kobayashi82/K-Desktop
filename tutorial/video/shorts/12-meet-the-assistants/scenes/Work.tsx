import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../../../src/components/Chapter';
import { KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { Showcase } from '../../../src/components/Showcase';
import { ease, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

/** Three real clips side by side, each one popping in at its frame, with its caption. */
export const Trio: React.FC<{ clips: Array<[string, string, number]>; heading?: string; headingAt?: number; pause?: number }> = ({ clips, heading, headingAt = 0, pause = 0 }) => {
  const f = useCurrentFrame();
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      {heading && <div style={{ position: 'absolute', left: 0, right: 0, top: 150, textAlign: 'center', fontSize: 44, fontWeight: 800, color: C.text, opacity: ease(f, headingAt, headingAt + 14) }}>{heading}</div>}
      {clips.map(([clip, label, at], i) => {
        const k = pop(f, at, { damping: 14 });
        return (
          <div key={clip} style={{ position: 'absolute', left: 330 + 630 * i, top: 500, transform: `translate(-50%, -50%) scale(${0.75 + 0.25 * k})`, opacity: Math.min(1, k * 1.4) }}>
            <Showcase clip={clip} width={580} label={label} from={at} pause={pause} />
          </div>
        );
      })}
      {clips.map(([clip, , at]) => <Sfx key={clip} at={at} name="pop" volume={0.18} />)}
    </AbsoluteFill>
  );
};

/** Your commands, done where you can see them, each one its own way. */
const Gestures: React.FC = () => {
  const B = useBeats();
  const g = B('gestures');
  return (
    <Trio heading={t('Your commands, where you can see them')} headingAt={g.cue('does') - 10} pause={40} clips={[
      ['Lander/window.minimize', t('Lander'), g.cue('does') - 8],
      ['Kobii/window.move', t('Kobii'), g.cue('move') - 8],
      ['Kumo/window.move', t('Kumo'), g.cue('wrap') - 8],
    ]} />
  );
};

/** What it is doing, always in sight. */
const States: React.FC = () => {
  const B = useBeats();
  const s = B('states');
  return (
    <Trio clips={[
      ['Lander/state.listening', t('Listening'), s.cue('state') - 8],
      ['Kobii/state.thinking', t('Thinking'), s.cue('thinking') - 8],
      ['Kumo/state.speaking', t('Speaking'), s.cue('speaking') - 8],
    ]} />
  );
};

// The assistant actions of the catalog (en.json, action.Assistant*.name / .desc).
const ACTIONS: Array<[string, string, string, string, string]> = [
  ['ask', 'chat_bubbles_question_24_filled', 'teal', t('Assistant: ask'), t('As if you said it out loud')],
  ['say', 'comment_24_filled', 'orange', t('Assistant: say'), t('In its bubble, without the AI')],
  ['chat', 'chat_24_filled', 'blue', t('Assistant: chat'), t('Opens its chat window')],
  ['tool', 'wrench_24_filled', 'purple', t('Assistant: use a tool'), t('Without the AI; the answer goes to {result}')],
  ['extra', 'sparkle_24_filled', 'pink', t('Assistant: action'), t('One of its own, like manual mode')],
];

/** The assistant actions you can put in a command. */
const Actions: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const a = B('actions');
  return (
    <AbsoluteFill style={{ fontFamily: SANS }}>
      <div style={{ position: 'absolute', left: 0, right: 0, top: 120, textAlign: 'center', fontSize: 44, fontWeight: 800, color: C.text, opacity: ease(f, a.cue('actions') - 10, a.cue('actions') + 4) }}>{t('Assistant actions')}</div>
      <div style={{ position: 'absolute', left: 360, right: 360, top: 220, display: 'flex', flexDirection: 'column', gap: 18 }}>
        {ACTIONS.map(([cue, icon, color, name, desc]) => {
          const k = pop(f, a.cue(cue) - 6, { damping: 15 });
          return (
            <div key={cue} style={{
              display: 'flex', alignItems: 'center', gap: 24, padding: '18px 26px', borderRadius: 20, background: 'rgba(30,30,36,0.94)', border: `1.5px solid ${C.line2}`,
              boxShadow: '0 20px 50px rgba(0,0,0,0.4)', opacity: Math.min(1, k * 1.4), transform: `translateX(${(1 - k) * 80}px)`,
            }}>
              <KdIcon icon={icon} color={color} size={70} />
              <div>
                <div style={{ fontSize: 32, fontWeight: 700, color: C.text }}>{name}</div>
                <div style={{ fontSize: 23, color: C.dim, marginTop: 4 }}>{desc}</div>
              </div>
            </div>
          );
        })}
      </div>
      {ACTIONS.map(([cue]) => <Sfx key={cue} at={a.cue(cue) - 6} name="pop-high" volume={0.14} />)}
    </AbsoluteFill>
  );
};

/** Chapter «How it works». */
export const Work: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const at = (id: string) => B(id).from - 6;
  return (
    <AbsoluteFill>
      <Part from={0} to={at('states')} first><Gestures /></Part>
      <Part from={at('states')} to={at('actions')}><States /></Part>
      <Part from={at('actions')} to={durationInFrames} last><Actions /></Part>
    </AbsoluteFill>
  );
};
