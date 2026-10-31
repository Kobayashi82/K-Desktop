import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Part } from '../../../src/components/Chapter';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats, useChapter } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';
import { Chat, Msg } from './Chat';

/** Commands, the assistant, a voice note and a photo, in the chat. */
const Talk: React.FC = () => {
  const B = useBeats();
  const c = B('commands');
  const a = B('assistant');
  const msgs: Msg[] = [
    { at: c.cue('slash') - 6, me: true, text: '/commands' },
    { at: c.cue('menu') - 6, text: t('Which command shall I run?'), buttons: [t('Gaming mode'), t('Movie night'), t('Work mode'), t('Lock the PC')], hot: 0 },
    { at: c.cue('runs') + 8, text: '✓ ' + t('Gaming mode') },
    { at: a.cue('assistant') - 6, me: true, text: t('What is using the most memory?') },
    { at: a.cue('assistant') + 26, text: t('Chrome, with 3.1 GB. Then Steam, with 1.2 GB.') },
    { at: a.cue('voice') - 6, me: true, voice: 4 },
    { at: a.cue('voice') + 22, text: t('“Open Spotify and play some jazz”') },
    { at: a.cue('answer') - 4, voice: 3, done: '✓ ' + t('Opened Spotify') },
    { at: a.cue('photo') - 6, me: true, photo: true },
    { at: a.cue('photo') + 28, text: t('A sunny meadow under a clear blue sky.') },
  ];
  return (
    <AbsoluteFill>
      <Chat msgs={msgs} width={860} height={900} style={{ left: 530, top: 90 }} />
      {msgs.map(m => <Sfx key={m.at} at={m.at} name={m.me ? 'pop' : 'pop-high'} volume={0.15} />)}
    </AbsoluteFill>
  );
};

const Card: React.FC<{ at: number; top: number; icon: string; color: string; name: string; desc: string; children?: React.ReactNode }> = ({ at, top, icon, color, name, desc, children }) => {
  const f = useCurrentFrame();
  const k = glide(f, at, 22);
  return (
    <div style={{
      position: 'absolute', left: 120, top, width: 900, padding: '26px 30px', borderRadius: 24, boxSizing: 'border-box', fontFamily: SANS,
      background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
      opacity: k, transform: `translateX(${(1 - k) * -60}px)`,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
        <KdIcon icon={icon} color={color} size={62} />
        <div>
          <div style={{ fontSize: 30, fontWeight: 700, color: C.text }}>{name}</div>
          <div style={{ fontSize: 19, color: C.dim, marginTop: 4 }}>{desc}</div>
        </div>
      </div>
      {children}
    </div>
  );
};

/** «Send via Telegram» and «A Telegram message arrives», with what reaches you. */
const Auto: React.FC = () => {
  const f = useCurrentFrame();
  const B = useBeats();
  const a = B('auto');
  const kinds = [
    { cue: 'send', icon: 'text_t_24_regular', label: t('A text') },
    { cue: 'file', icon: 'document_24_regular', label: t('A file') },
    { cue: 'screenshot', icon: 'screenshot_24_regular', label: t('A screenshot') },
  ];
  const msgs: Msg[] = [
    { at: a.cue('send') + 6, text: t('Backup finished: 12.4 GB copied.') },
    { at: a.cue('file') - 2, file: 'invoice-october.pdf' },
    { at: a.cue('screenshot') + 6, photo: true },
    { at: a.cue('event') + 6, me: true, text: t('note: call the plumber') },
  ];
  return (
    <AbsoluteFill>
      <Card at={a.from - 4} top={170} icon="send_24_filled" color="sky" name={t('Send via Telegram')}
        desc={t('Sends you (or a contact) a text, a file, a screenshot or what’s on the clipboard.')}>
        <div style={{ display: 'flex', gap: 14, marginTop: 22 }}>
          {kinds.map(k => {
            const p = pop(f, a.cue(k.cue) - 6, { damping: 14 });
            return (
              <div key={k.cue} style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '10px 18px', borderRadius: 12, background: C.accentSoft, color: C.accentText, fontSize: 22, fontWeight: 600, opacity: Math.min(1, p * 1.4), transform: `scale(${0.8 + 0.2 * p})` }}>
                <Fluent name={k.icon} size={24} color={C.accentText} /> {k.label}
              </div>
            );
          })}
        </div>
      </Card>
      <Card at={a.cue('event') - 12} top={560} icon="send_24_filled" color="purple" name={t('A Telegram message arrives')}
        desc={t('Yours or a contact’s, with some words, a voice note or a file.')}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginTop: 20, fontSize: 22, color: C.text }}>
          <Fluent name="arrow_right_24_filled" size={26} color={C.accentText} />
          <KdIcon icon="note_24_filled" color="yellow" size={36} /> {t('Jot it down in a note')}
        </div>
      </Card>
      <Chat msgs={msgs} width={720} height={820} style={{ left: 1090, top: 130 }} />
      {msgs.map(m => <Sfx key={m.at} at={m.at} name="pop-high" volume={0.15} />)}
    </AbsoluteFill>
  );
};

/** Chapter «What it can do». */
export const Use: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const a = B('auto');
  return (
    <AbsoluteFill>
      <Part from={0} to={a.from - 6} first><Talk /></Part>
      <Part from={a.from - 6} to={durationInFrames} last><Auto /></Part>
    </AbsoluteFill>
  );
};
