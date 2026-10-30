import React from 'react';
import { Img, staticFile } from 'remotion';
import { C, SANS } from '../theme';
import { Fluent } from './Icon';
import { t } from '../lib/i18n';

export const NAV = [
  { id: 'commands', label: t('Commands'), icon: 'flash_24_regular' },
  { id: 'events', label: t('Events'), icon: 'arrow_forward_down_lightning_24_regular' },
  { id: 'quickmenu', label: t('QuickMenu'), icon: 'text_bullet_list_tree_24_regular' },
  { id: 'kboard', label: t('K-Board'), icon: 'keyboard_layout_float_24_regular' },
  { id: 'macros', label: t('Macros'), icon: 'record_24_regular' },
  { id: 'history', label: t('History'), icon: 'history_24_regular' },
  { id: 'achievements', label: t('Achievements'), icon: 'trophy_24_regular' },
  { id: 'assistant', label: t('Assistant'), icon: 'bot_24_regular' },
  { id: 'settings', label: t('Settings'), icon: 'settings_24_regular' },
  { id: 'about', label: t('About'), icon: 'info_24_regular' },
];

/** Window frame of Windows 11 (title, minimise, maximise, close) around any content. */
export const WinFrame: React.FC<{ title?: string; icon?: React.ReactNode; width: number; height: number; children?: React.ReactNode; style?: React.CSSProperties; bar?: number; bg?: string; light?: boolean }> = ({ title, icon, width, height, children, style, bar = 46, bg = C.window, light }) => (
  <div style={{
    position: 'absolute', width, height, borderRadius: 12, overflow: 'hidden', background: bg,
    boxShadow: '0 30px 80px rgba(0,0,0,0.55), 0 0 0 1px rgba(255,255,255,0.08)', ...style,
  }}>
    <div style={{ height: bar, display: 'flex', alignItems: 'center', padding: '0 0 0 16px', gap: 12, color: light ? '#222' : C.text, fontFamily: SANS, fontSize: 14 }}>
      {icon}
      <span style={{ flex: 1, opacity: 0.9 }}>{title}</span>
      {['subtract_24_regular', 'maximize_24_regular', 'dismiss_24_regular'].map(i => (
        <div key={i} style={{ width: 54, height: bar, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <Fluent name={i} size={15} color={light ? '#333' : C.text} />
        </div>
      ))}
    </div>
    <div style={{ position: 'absolute', top: bar, left: 0, right: 0, bottom: 0 }}>{children}</div>
  </div>
);

/** K-Desktop's main window: title bar, the side menu on `page`, and the page itself. */
export const KdWindow: React.FC<{ page: string; width?: number; height?: number; children?: React.ReactNode; style?: React.CSSProperties; assistant?: boolean }> = ({ page, width = 1560, height = 940, children, style, assistant = true }) => (
  <WinFrame width={width} height={height} title={t('K-Desktop 5.0')} style={style} bg="#1c1c1f"
    icon={<Img src={staticFile('brand/icon-64.png')} style={{ width: 18, height: 18 }} />}>
    <div style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: 250, background: '#1c1c1f', padding: '6px 14px', boxSizing: 'border-box' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '8px 8px 26px' }}>
        <Img src={staticFile('brand/icon-192.png')} style={{ width: 40, height: 40 }} />
        <span style={{ fontFamily: SANS, fontWeight: 700, fontSize: 21, color: C.text }}>{t('K-Desktop')}</span>
        <span style={{ fontFamily: SANS, fontWeight: 700, fontSize: 14, color: C.accentText, marginTop: 4 }}>5.0</span>
      </div>
      {NAV.filter(n => assistant || n.id !== 'assistant').map(n => {
        const active = n.id === page;
        return (
          <div key={n.id} style={{
            position: 'relative', display: 'flex', alignItems: 'center', gap: 14, height: 42, padding: '0 14px', borderRadius: 8, marginBottom: 2,
            background: active ? 'rgba(255,255,255,0.08)' : 'transparent', fontFamily: SANS, fontSize: 15, color: C.text,
          }}>
            {active && <div style={{ position: 'absolute', left: 0, top: 11, bottom: 11, width: 3, borderRadius: 2, background: C.accent }} />}
            <Fluent name={n.icon} size={19} color={C.text} />
            {n.label}
          </div>
        );
      })}
      <div style={{ position: 'absolute', left: 14, right: 14, bottom: 14, height: 52, borderRadius: 10, background: 'rgba(255,255,255,0.05)', display: 'flex', alignItems: 'center', justifyContent: 'space-around' }}>
        <Fluent name="power_24_regular" size={19} color="#FF6B6B" />
        <Fluent name="plug_connected_24_regular" size={19} color={C.dim} />
        <Fluent name="phone_24_regular" size={19} color={C.dim} />
        <Fluent name="mic_off_24_regular" size={19} color={C.dim} />
        {assistant && <Fluent name="bot_24_regular" size={19} color={C.dim} />}
      </div>
    </div>
    <div style={{ position: 'absolute', left: 250, top: 0, right: 0, bottom: 0, background: '#202024', borderTopLeftRadius: 10, overflow: 'hidden' }}>
      {children}
    </div>
  </WinFrame>
);
