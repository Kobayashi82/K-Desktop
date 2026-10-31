import React from 'react';
import { Img, staticFile } from 'remotion';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { t } from '../../../src/lib/i18n';
import { C, SANS } from '../../../src/theme';

/** A Windows notification from K-Desktop: app line, title, text and optional buttons; `show` slides it in. */
export const Toast: React.FC<{ title: string; text: string; icon: string; color: string; buttons?: string[]; show: number; scale?: number; style?: React.CSSProperties }> = ({ title, text, icon, color, buttons = [], show, scale = 1.4, style }) => (
  <div style={{
    position: 'absolute', width: 380 * scale, padding: `${14 * scale}px ${16 * scale}px`, borderRadius: 10 * scale, boxSizing: 'border-box', fontFamily: SANS,
    background: 'rgba(36,36,40,0.97)', border: '1px solid rgba(255,255,255,0.12)', boxShadow: `0 ${16 * scale}px ${40 * scale}px rgba(0,0,0,0.5)`,
    opacity: Math.min(1, show * 1.4), transform: `translateX(${(1 - show) * 120}px)`, ...style,
  }}>
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 * scale, fontSize: 12 * scale, color: C.dim, marginBottom: 10 * scale }}>
      <Img src={staticFile('brand/icon-64.png')} style={{ width: 16 * scale, height: 16 * scale }} /> K-Desktop
    </div>
    <div style={{ display: 'flex', gap: 12 * scale }}>
      <KdIcon icon={icon} color={color} size={40 * scale} />
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontSize: 15 * scale, fontWeight: 600, color: C.text }}>{title}</div>
        <div style={{ fontSize: 13 * scale, color: C.dim, marginTop: 3 * scale }}>{text}</div>
      </div>
    </div>
    {buttons.length > 0 && (
      <div style={{ display: 'flex', gap: 8 * scale, marginTop: 12 * scale }}>
        {buttons.map((b, i) => (
          <div key={b} style={{
            flex: 1, height: 32 * scale, borderRadius: 5 * scale, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6 * scale,
            fontSize: 13 * scale, color: i === 0 ? '#1a1306' : C.text, background: i === 0 ? C.accentText : 'rgba(255,255,255,0.08)',
          }}>{i === 0 && <Fluent name="video_24_regular" size={16 * scale} color="#1a1306" />}{b}</div>
        ))}
      </div>
    )}
  </div>
);

/** What an assistant says, in a speech bubble (no character: the video doesn't stage one). */
export const SaysBubble: React.FC<{ text: string; show: number; style?: React.CSSProperties }> = ({ text, show, style }) => (
  <div style={{ position: 'absolute', display: 'flex', alignItems: 'flex-end', gap: 16, opacity: Math.min(1, show * 1.4), transform: `scale(${0.85 + 0.15 * show})`, ...style }}>
    <KdIcon icon="bot_24_filled" color="purple" size={70} />
    <div style={{
      position: 'relative', padding: '18px 26px', borderRadius: '24px 24px 24px 6px', background: '#f4f4f6', color: '#16161a',
      fontFamily: SANS, fontSize: 28, fontWeight: 600, boxShadow: '0 20px 50px rgba(0,0,0,0.45)', whiteSpace: 'nowrap',
    }}>{text}</div>
  </div>
);

/** The reminder of the examples, as App.ReminderText writes it: «Team stand-up», in 10 minutes. */
export const REMINDER = { title: t('Team stand-up'), text: t('Team stand-up · in 10 min (9:30 AM)') };
