import React from 'react';
import { useCurrentFrame } from 'remotion';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { WinFrame } from '../../../src/components/KdWindow';
import { pop, rand } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { C, SANS } from '../../../src/theme';

/** One message of the chat, shown from frame `at`: yours (right) or the bot's (left). */
export type Msg = {
  at: number; me?: boolean; text?: string; buttons?: string[]; hot?: number;
  voice?: number; photo?: boolean; file?: string; done?: string;
};

const Voice: React.FC<{ secs: number; me?: boolean; seed: number }> = ({ secs, me, seed }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
    <div style={{ width: 40, height: 40, borderRadius: 20, background: me ? '#fff' : '#5aa9ff', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      <Fluent name="play_24_filled" size={20} color={me ? '#2b5278' : '#fff'} />
    </div>
    <div style={{ display: 'flex', alignItems: 'center', gap: 3, height: 30 }}>
      {Array.from({ length: 30 }, (_, i) => <div key={i} style={{ width: 3, borderRadius: 2, height: 6 + 22 * rand(seed * 31 + i), background: me ? 'rgba(255,255,255,0.8)' : 'rgba(90,169,255,0.9)' }} />)}
    </div>
    <span style={{ fontSize: 19, opacity: 0.75 }}>0:{String(secs).padStart(2, '0')}</span>
  </div>
);

/** A Telegram chat on the PC with your bot: the messages appear as their frames come, newest at the bottom. */
export const Chat: React.FC<{ msgs: Msg[]; width?: number; height?: number; style?: React.CSSProperties; title?: string }> = ({ msgs, width = 760, height = 760, style, title = t('Alex’s PC') }) => {
  const f = useCurrentFrame();
  const shown = msgs.filter(m => f >= m.at);
  return (
    <WinFrame width={width} height={height} title="Telegram" bar={40} bg="#0e1621" icon={<Fluent name="send_24_filled" size={18} color="#5aa9ff" />} style={style}>
      <div style={{ height: 76, display: 'flex', alignItems: 'center', gap: 14, padding: '0 20px', background: '#17212b', fontFamily: SANS }}>
        <KdIcon icon="desktop_24_filled" color="gold" size={50} />
        <div>
          <div style={{ fontSize: 24, fontWeight: 700, color: C.text }}>{title}</div>
          <div style={{ fontSize: 18, color: '#6c8eb0' }}>{t('bot')}</div>
        </div>
      </div>
      <div style={{ position: 'absolute', top: 76, left: 0, right: 0, bottom: 58, display: 'flex', flexDirection: 'column', justifyContent: 'flex-end', gap: 12, padding: '0 22px 14px', overflow: 'hidden', fontFamily: SANS }}>
        {shown.map((m, i) => {
          const k = pop(f, m.at, { damping: 15, stiffness: 160 });
          return (
            <div key={i} style={{ alignSelf: m.me ? 'flex-end' : 'flex-start', maxWidth: '80%', opacity: Math.min(1, k * 1.5), transform: `translateY(${(1 - k) * 24}px) scale(${0.92 + 0.08 * k})`, transformOrigin: m.me ? 'right bottom' : 'left bottom' }}>
              <div style={{
                padding: m.photo ? 6 : '11px 16px', borderRadius: m.me ? '16px 16px 4px 16px' : '16px 16px 16px 4px', fontSize: 25, lineHeight: 1.35,
                background: m.me ? '#2b5278' : '#182533', color: C.text, whiteSpace: 'pre-line',
              }}>
                {m.photo && <div style={{ width: 380, height: 250, borderRadius: 12, background: 'linear-gradient(160deg, #7fb2ff 0%, #3b6ea8 40%, #2d5a3a 41%, #3f7a4a 100%)', position: 'relative', overflow: 'hidden' }}>
                  <div style={{ position: 'absolute', left: 250, top: 36, width: 56, height: 56, borderRadius: 28, background: '#ffe27a' }} />
                </div>}
                {m.voice !== undefined && <Voice secs={m.voice} me={m.me} seed={i + 1} />}
                {m.file && <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}><KdIcon icon="document_pdf_24_filled" color="red" size={44} /><span>{m.file}</span></div>}
                {m.text && <div style={{ marginTop: m.photo || m.voice !== undefined ? 8 : 0 }}>{m.text}</div>}
                {m.done && <div style={{ marginTop: 6, fontSize: 21, color: '#7ec27e' }}>{m.done}</div>}
              </div>
              {m.buttons && (
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 6, marginTop: 6 }}>
                  {m.buttons.map((b, j) => (
                    <div key={b} style={{ padding: '12px 16px', borderRadius: 10, textAlign: 'center', fontSize: 22, color: C.text, background: j === m.hot ? 'rgba(90,169,255,0.45)' : 'rgba(43,82,120,0.55)' }}>{b}</div>
                  ))}
                </div>
              )}
            </div>
          );
        })}
      </div>
      <div style={{ position: 'absolute', left: 0, right: 0, bottom: 0, height: 58, background: '#17212b', display: 'flex', alignItems: 'center', gap: 14, padding: '0 20px', fontFamily: SANS, fontSize: 22, color: '#6c7883' }}>
        <Fluent name="attach_24_regular" size={24} color="#6c7883" />
        <span style={{ flex: 1 }}>{t('Write a message…')}</span>
        <Fluent name="mic_24_regular" size={24} color="#6c7883" />
      </div>
    </WinFrame>
  );
};
