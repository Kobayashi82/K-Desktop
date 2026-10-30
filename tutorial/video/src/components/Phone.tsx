import React from 'react';
import { Img, staticFile, useCurrentFrame } from 'remotion';
import { ease, OUT } from '../lib/anim';
import { useLang } from './Capture';

/** A phone; its screen is a web app screenshot (public/webapp/<lang>/<name>.png) or anything as children. */
export const Phone: React.FC<{ width?: number; screen?: string; children?: React.ReactNode; style?: React.CSSProperties; android?: boolean }> = ({ width = 420, screen, children, style, android }) => {
  const lang = useLang();
  const height = width * (892 / 412);
  const bezel = width * 0.035;
  return (
    <div style={{
      position: 'relative', width: width + bezel * 2, height: height + bezel * 2, borderRadius: width * 0.13, background: android ? '#1b1d22' : 'linear-gradient(145deg,#3a3a42,#16161a)',
      boxShadow: '0 50px 100px rgba(0,0,0,0.6), inset 0 0 0 2px rgba(255,255,255,0.12)', ...style,
    }}>
      <div style={{ position: 'absolute', left: bezel, top: bezel, width, height, borderRadius: width * 0.105, overflow: 'hidden', background: '#0e0e10' }}>
        {screen && <Img src={staticFile(`webapp/${lang}/${screen}.png`)} style={{ width, height, display: 'block' }} />}
        {children}
      </div>
      <div style={{ position: 'absolute', left: '50%', top: bezel + width * 0.025, width: width * 0.06, height: width * 0.06, marginLeft: -width * 0.03, borderRadius: '50%', background: '#050506' }} />
    </div>
  );
};

/** Screens that slide in one after another (like the web app's views): [frame, screen] pairs. */
export const PhoneScreens: React.FC<{ width: number; screens: Array<[number, string]> }> = ({ width, screens }) => {
  const f = useCurrentFrame();
  const lang = useLang();
  const height = width * (892 / 412);
  let cur = 0;
  screens.forEach(([at], i) => { if (f >= at) cur = i; });
  const k = cur === 0 ? 1 : ease(f, screens[cur][0], screens[cur][0] + 14, OUT);
  const img = (name: string, style: React.CSSProperties) => <Img key={name} src={staticFile(`webapp/${lang}/${name}.png`)} style={{ position: 'absolute', left: 0, top: 0, width, height, ...style }} />;
  return (
    <>
      {cur > 0 && k < 1 && img(screens[cur - 1][1], { transform: `translateX(${-k * width * 0.3}px)`, opacity: 1 - 0.5 * k })}
      {img(screens[cur][1], { transform: `translateX(${(1 - k) * width}px)` })}
    </>
  );
};
