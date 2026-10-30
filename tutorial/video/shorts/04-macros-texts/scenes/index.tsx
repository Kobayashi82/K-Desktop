import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { CaptureShot } from '../../../src/components/Capture';
import { Part } from '../../../src/components/Chapter';
import { KdIcon } from '../../../src/components/Icon';
import { WinFrame } from '../../../src/components/KdWindow';
import { Keycap } from '../../../src/components/Keys';
import { MouseShape } from '../../../src/components/Props';
import { ShortAgenda, ShortEnd, ShortTitle } from '../../../src/components/Short';
import { Sfx } from '../../../src/components/Sfx';
import { ease, glide, IN, inOut, keys, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { Macros } from '../../../src/scenes/Macros';
import type { Short } from '../../../src/shorts';
import { C, MONO, MOODS, SANS } from '../../../src/theme';

/** Hands on the mouse and keys, a phrase typed once more; the title and what the video covers. */
const Hook: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const idea = B('idea');
  const plan = B('plan');
  const hands = pop(f, idea.from + 2, { damping: 14 });
  const words = pop(f, idea.cue('words') - 6, { damping: 14 });
  const away = ease(f, idea.to - 8, idea.to + 12, IN);
  const phrase = t('Best regards, Alex');
  const typed = phrase.slice(0, Math.max(0, Math.floor((f - idea.cue('words')) / 1.6)) % (phrase.length + 8));
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', left: 560, top: 470, transform: `translate(-50%, -50%) scale(${hands * (1 - 0.3 * away)})`, opacity: 1 - away, display: 'flex', alignItems: 'center', gap: 30 }}>
        <MouseShape scale={0.75} left={inOut(f, idea.cue('hands'), idea.cue('hands') + 20, 4)} />
        <div style={{ display: 'flex', gap: 14 }}>
          {['Ctrl', 'C'].map((k, i) => <Keycap key={k} label={k} height={96} width={k.length > 1 ? 150 : 96} size={32} down={inOut(f, idea.cue('hands') + 14 + i * 4, idea.cue('hands') + 34, 3)} />)}
        </div>
      </div>
      <div style={{
        position: 'absolute', left: 1360, top: 470, transform: `translate(-50%, -50%) scale(${words * (1 - 0.3 * away)})`, opacity: 1 - away,
        width: 620, padding: '30px 36px', borderRadius: 22, background: 'rgba(30,30,36,0.95)', border: `1px solid ${C.line2}`, fontFamily: MONO, fontSize: 34, color: C.text,
      }}>
        {typed}<span style={{ display: 'inline-block', width: 3, height: 38, background: C.accentText, marginLeft: 3, verticalAlign: 'middle' }} />
      </div>
      {f > idea.to - 6 && <ShortTitle at={idea.to - 4} y={keys(f, [[plan.from - 10, 440], [plan.from + 16, 380]])} />}
      {f > plan.from - 10 && (
        <ShortAgenda y={820} items={[
          { label: t('Macros'), icon: 'record_24_filled', color: 'red', at: plan.cue('macros') },
          { label: t('Texts'), icon: 'text_expand_24_filled', color: 'gold', at: plan.cue('texts') },
        ]} />
      )}
      <Sfx at={idea.from + 2} name="pop" volume={0.22} />
      <Sfx at={idea.cue('words') - 6} name="pop" volume={0.22} />
    </AbsoluteFill>
  );
};

/** A Notepad window where an abbreviation is typed and, a moment later, swapped for its text. */
const TypingDemo: React.FC<{ before: string; abbr: string; full: string; typeAt: number; swapAt: number; from: number }> = ({ before, abbr, full, typeAt, swapAt, from }) => {
  const f = useCurrentFrame();
  const win = glide(f, from, 24);
  const swapped = f >= swapAt;
  const shownAbbr = abbr.slice(0, Math.max(0, Math.floor((f - typeAt) / 4)));
  const flash = inOut(f, swapAt, swapAt + 26, 6);
  const text = swapped ? full : shownAbbr;
  return (
    <div style={{ position: 'absolute', left: 260, top: 150, transform: `translateY(${(1 - win) * 60}px)`, opacity: win }}>
      <WinFrame width={1400} height={640} title={t('Untitled - Notepad')} bg="#202228" icon={<KdIcon icon="document_text_24_filled" color="sky" size={20} />}>
        <div style={{ padding: '30px 40px', fontFamily: MONO, fontSize: 38, lineHeight: 1.5, color: C.text, whiteSpace: 'pre-wrap' }}>
          {before}
          <span style={{ background: `rgba(242,193,74,${0.28 * flash})`, borderRadius: 6, color: !swapped && shownAbbr ? C.accentText : C.text }}>{text}</span>
          <span style={{ display: 'inline-block', width: 3, height: 44, background: C.accentText, marginLeft: 2, verticalAlign: 'middle', opacity: Math.floor(f / 15) % 2 ? 1 : 0.3 }} />
        </div>
      </WinFrame>
    </div>
  );
};

// The real Texts page (tutorial/captures/en/snippets.png).
const TEXT_BOX: [number, number, number, number] = [576, 276, 1290, 142];

/** Chapter «Texts»: the real page, the swap in a program, and what goes inside a text. */
const Texts: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const what = B('what');
  const type = B('type');
  const fields = B('fields');
  return (
    <AbsoluteFill>
      <Part from={0} to={type.from - 6} first>
        <CaptureShot name="snippets"
          shots={[{ at: 0, x: 960, y: 540, s: 0.96, rx: 6 }, { at: 6, dur: 40, x: 960, y: 480, s: 1.1, rx: 0 }, { at: what.cue('abbr') - 20, dur: 26, x: 800, y: 300, s: 1.45 }]}
          focus={[
            { from: what.cue('address') - 4, to: what.cue('signature') - 2, rect: [262, 312, 282, 66] },
            { from: what.cue('signature') - 2, to: what.cue('abbr') - 6, rect: [262, 687, 282, 66] },
            { from: what.cue('abbr') - 6, to: type.from, rect: [652, 110, 1040, 44], label: ';addr', at: 'bottom' },
          ]} />
      </Part>
      <Part from={type.from - 6} to={fields.from - 6}>
        <TypingDemo from={type.from - 6} before={t('Please send it to:\n')} abbr=";addr" full={'42 Baker Street\nLondon NW1 6XE'} typeAt={type.cue('type') - 4} swapAt={type.cue('swaps')} />
      </Part>
      <Part from={fields.from - 6} to={durationInFrames} last>
        <CaptureShot name="snippets"
          shots={[{ at: fields.from - 6, x: 1200, y: 420, s: 1.2 }, { at: fields.from, dur: 30, x: 1200, y: 400, s: 1.35 }, { at: fields.cue('try') - 16, dur: 26, x: 1200, y: 560, s: 1.35 }]}
          focus={[
            { from: fields.cue('brace') - 4, to: fields.cue('try') - 6, rect: [TEXT_BOX[0], TEXT_BOX[1], TEXT_BOX[2], TEXT_BOX[3] + 34], label: t('{ date, time, clipboard · {cursor}'), at: 'top' },
            { from: fields.cue('try') - 6, to: fields.cue('options') - 6, rect: [576, 544, 1290, 64] },
            { from: fields.cue('options') - 6, to: durationInFrames, rect: [562, 660, 1318, 58], label: t('Only in these applications'), at: 'top' },
          ]} />
      </Part>
      <Sfx at={type.cue('swaps')} name="chime" volume={0.22} />
    </AbsoluteFill>
  );
};

/** Chapter «One more thing»: the abbreviation in capitals writes the text in capitals. */
const Tip: React.FC = () => {
  const B = useBeats();
  const tip = B('tip');
  return (
    <AbsoluteFill>
      <TypingDemo from={0} before={t('Thanks for your help.\n\n')} abbr=";SIG" full={t('BEST REGARDS,\nALEX')} typeAt={tip.cue('caps') - 4} swapAt={tip.cue('out') - 8} />
      <Sfx at={tip.cue('out') - 8} name="chime" volume={0.22} />
    </AbsoluteFill>
  );
};

/** Tutorial 04, «Macros and Texts»: the long video's macros, and Texts with the real page. */
export const MACROS_TEXTS: Short = {
  scenes: { hook: Hook, macros: Macros, texts: Texts, tip: Tip, end: ShortEnd },
  moods: { hook: MOODS.intro, macros: MOODS.macros, texts: MOODS.launcher, tip: MOODS.kboard, end: MOODS.outro },
};
