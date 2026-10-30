import React from 'react';
import { C } from '../theme';
import { MAIN_MENU, QuickMenuPanel } from './QuickMenu';

/** A mouse seen from above, with its two side buttons; `side` presses the front one, `glow` lights it. */
export const MouseShape: React.FC<{ side?: number; glow?: number; left?: number; right?: number; wheel?: number; scale?: number }> = ({ side = 0, glow = 0, left = 0, right = 0, wheel = 0, scale = 1 }) => (
  <svg width={220 * scale} height={340 * scale} viewBox="0 0 220 340" style={{ overflow: 'visible', filter: 'drop-shadow(0 24px 40px rgba(0,0,0,0.5))' }}>
    <defs>
      <linearGradient id="mouseBody" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stopColor="#4a4a54" />
        <stop offset="1" stopColor="#1e1e24" />
      </linearGradient>
    </defs>
    <path d="M110 10 C 190 10, 205 90, 205 170 C 205 270, 165 330, 110 330 C 55 330, 15 270, 15 170 C 15 90, 30 10, 110 10 Z" fill="url(#mouseBody)" stroke="rgba(255,255,255,0.15)" strokeWidth="2" />
    <path d="M110 12 L110 120" stroke="rgba(0,0,0,0.5)" strokeWidth="3" />
    <path d="M110 12 C 40 14, 22 80, 18 120 L 108 120 Z" fill={`rgba(242,193,74,${left * 0.8})`} />
    <path d="M110 12 C 180 14, 198 80, 202 120 L 112 120 Z" fill={`rgba(85,210,255,${right * 0.8})`} />
    <path d="M17 122 L 203 122" stroke="rgba(0,0,0,0.35)" strokeWidth="2" />
    <rect x="100" y="42" width="20" height="48" rx="10" fill="#15151a" stroke={wheel > 0 ? C.gold1 : 'rgba(255,255,255,0.25)'} strokeWidth="2" />
    <rect x="106" y={52 + ((wheel * 30) % 20)} width="8" height="6" rx="2" fill="rgba(255,255,255,0.4)" />
    {/* Side buttons (Mouse 4 behind, Mouse 5 in front) */}
    <rect x={4 + side * 4} y="150" width="16" height="44" rx="7" fill={glow > 0 ? `rgba(255,217,112,${0.4 + 0.6 * glow})` : '#33333b'} stroke="rgba(255,255,255,0.2)"
      style={{ filter: glow > 0 ? `drop-shadow(0 0 ${14 * glow}px ${C.gold1})` : undefined }} />
    <rect x="6" y="200" width="14" height="40" rx="7" fill="#33333b" stroke="rgba(255,255,255,0.2)" />
  </svg>
);

/** The QuickMenu, small, for quick appearances. */
export const MiniMenu: React.FC<{ appear?: number }> = ({ appear = 1 }) => <QuickMenuPanel items={MAIN_MENU.slice(0, 6)} scale={1.3} appear={appear} hot={2} />;
