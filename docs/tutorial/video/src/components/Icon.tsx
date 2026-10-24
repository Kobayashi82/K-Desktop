import React from 'react';
import { ICONS } from '../generated/icons';
import { PALETTE, PaletteKey } from '../theme';

/** A Fluent System Icon by its file name («flash_24_filled»), painted with `color`. */
export const Fluent: React.FC<{ name: string; size?: number; color?: string; style?: React.CSSProperties }> = ({ name, size = 24, color = 'currentColor', style }) => {
  const icon = ICONS[name];
  if (!icon) return <span style={{ display: 'inline-block', width: size, height: size, ...style }} />;
  return (
    <svg width={size} height={size} viewBox={`0 0 ${icon.size} ${icon.size}`} fill={color} style={{ display: 'block', flexShrink: 0, ...style }}
      dangerouslySetInnerHTML={{ __html: icon.body }} />
  );
};

/**
 * A K-Desktop 5 icon (KdIcons.cs): a rounded tile with the palette gradient, a soft shine on the upper half, a thin
 * darker edge and the white symbol with a small shadow.
 */
export const KdIcon: React.FC<{ icon: string; color?: PaletteKey | string; size?: number; glow?: number; style?: React.CSSProperties }> = ({ icon, color = 'blue', size = 48, glow = 0, style }) => {
  const [top, bottom] = PALETTE[color] ?? PALETTE.blue;
  const r = (11 / 48) * size;
  const glyph = (25 / 48) * size;
  return (
    <div style={{
      width: size, height: size, borderRadius: r, position: 'relative', flexShrink: 0,
      background: `linear-gradient(180deg, ${top}, ${bottom})`,
      boxShadow: `inset 0 0 0 1px rgba(0,0,0,0.24), 0 ${size * 0.06}px ${size * 0.18}px rgba(0,0,0,0.35)` + (glow ? `, 0 0 ${size * 0.6 * glow}px ${top}` : ''),
      display: 'flex', alignItems: 'center', justifyContent: 'center', ...style,
    }}>
      <div style={{ position: 'absolute', inset: 0, borderRadius: r, background: 'linear-gradient(180deg, rgba(255,255,255,0.28), rgba(255,255,255,0) 55%)' }} />
      <Fluent name={icon} size={glyph} color="white" style={{ filter: `drop-shadow(0 ${size * 0.025}px 0 rgba(0,0,0,0.22))`, position: 'relative' }} />
    </div>
  );
};
