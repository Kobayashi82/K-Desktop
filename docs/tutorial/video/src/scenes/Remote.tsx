import React from 'react';
import { AbsoluteFill, Img, staticFile, useCurrentFrame } from 'remotion';
import { useCapture } from '../components/Capture';
import { Part } from '../components/Chapter';
import { Cursor } from '../components/Cursor';
import { AppWindow, Wallpaper } from '../components/Desktop';
import { Node, Wire } from '../components/Diagram';
import { Fluent, KdIcon } from '../components/Icon';
import { Mono } from '../components/Keys';
import { Osd } from '../components/Osd';
import { Phone, PhoneScreens } from '../components/Phone';
import { Sfx } from '../components/Sfx';
import { ease, glide, inOut, keys, OUT, pop } from '../lib/anim';
import { BeatTimes, useBeats, useChapter } from '../lib/timeline';
import { C, MONO, SANS } from '../theme';

const PW = 400; // phone screen width

/** A finger touch on the phone's screen. */
const Touch: React.FC<{ x: number; y: number; show: number }> = ({ x, y, show }) => (
  <div style={{ position: 'absolute', left: x - 30, top: y - 30, width: 60, height: 60, borderRadius: 30, background: 'rgba(255,255,255,0.35)', border: '3px solid rgba(255,255,255,0.8)', opacity: show, transform: `scale(${0.7 + 0.3 * show})` }} />
);

/** The phone and the PC: pairing, then each tab of the app doing its thing on the PC. */
const PhonePart: React.FC<{ intro: BeatTimes; pair: BeatTimes; menus: BeatTimes; pad: BeatTimes; audio: BeatTimes; voicechat: BeatTimes }> = ({ intro, pair, menus, pad, audio, voicechat }) => {
  const f = useCurrentFrame();
  const web = useCapture('settings-web');
  const qr = useCapture('pair-qr');
  const phoneIn = pop(f, intro.cue('webapp') - 8, { damping: 15, stiffness: 120 });
  const scanAt = pair.cue('scan');
  const paired = scanAt + 50;
  const screens: Array<[number, string]> = [
    [0, 'pair'], [paired, 'all'], [menus.cue('qm') - 4, 'menus'], [menus.cue('all') - 4, 'all'],
    [pad.from - 4, 'pad'], [pad.cue('keyboard') - 4, 'pad-keys'], [audio.from - 4, 'audio'], [voicechat.cue('chat') - 4, 'assistant'],
  ];
  // The PC side changes with what the phone does.
  const pcMode = f < menus.from - 10 ? 'pair' : 'desk';
  const qrShow = inOut(f, scanAt - 20, paired + 6, 8);
  const scanning = inOut(f, scanAt, paired - 4, 6);
  const revoke = pair.cue('revoke');
  const deviceRow = inOut(f, paired + 4, menus.from, 8);
  const revoked = ease(f, revoke + 10, revoke + 20);
  // Touchpad: the finger draws a loop; the PC's pointer follows it.
  const padT = Math.max(0, f - pad.cue('touchpad'));
  const fx = 200 + Math.cos(padT / 14) * 90;
  const fy = 430 + Math.sin(padT / 10) * 120;
  const touch = inOut(f, pad.cue('touchpad'), pad.cue('keyboard') - 4, 6);
  const typed = 'Hello from the couch'.slice(0, Math.max(0, Math.floor((f - pad.cue('keyboard') - 6) / 2)));
  const volume = keys(f, [[audio.cue('volume'), 42], [audio.cue('volume') + 20, 64]]);
  const osd = Math.max(inOut(f, audio.cue('volume'), audio.cue('music') - 4, 6), inOut(f, audio.cue('music'), audio.cue('output') - 4, 6), inOut(f, audio.cue('output'), voicechat.from - 6, 6));
  const osdText = f < audio.cue('music') ? `Volume: ${Math.round(volume)} %` : f < audio.cue('output') ? 'Play / Pause' : 'Audio output: Headphones';
  const micAt = voicechat.cue('mic');
  const camera = { s: 1 + 0.03 * Math.sin(f / 60) };
  return (
    <AbsoluteFill>
      {/* The PC */}
      <div style={{ position: 'absolute', left: 120, top: 150, width: 1120, height: 640, transform: `scale(${camera.s})` }}>
        <div style={{ position: 'absolute', inset: 0, borderRadius: 20, overflow: 'hidden', border: '10px solid #2a2a30', boxShadow: '0 40px 90px rgba(0,0,0,0.5)', background: '#111' }}>
          {pcMode === 'pair' ? (
            <div style={{ position: 'absolute', inset: 0 }}>
              <div style={{ position: 'absolute', left: 0, top: 0, width: 1920, height: 1080, transform: 'scale(1.1) translate(-420px, -40px)', transformOrigin: '0 0' }}>
                <Img src={staticFile(web.file)} style={{ width: 1920, height: 1080 }} />
                {/* The paired phone, then revoked */}
                <div style={{ position: 'absolute', left: 571, top: 480, width: 750, height: 60 * (1 - revoked), overflow: 'hidden', display: 'flex', alignItems: 'center', gap: 14, padding: '0 14px', borderRadius: 10, background: `rgba(${revoked > 0 ? '255,107,107' : '255,255,255'},0.08)`, opacity: deviceRow, fontFamily: SANS, fontSize: 18, color: C.text }}>
                  <Fluent name="phone_24_filled" size={22} color={C.accentText} /> Alex’s phone <span style={{ color: C.dim, fontSize: 15 }}>· just now · 192.168.1.42</span>
                  <span style={{ marginLeft: 'auto', padding: '5px 12px', borderRadius: 8, fontSize: 15, background: f > revoke ? 'rgba(255,107,107,0.3)' : 'rgba(255,255,255,0.1)', color: f > revoke ? C.danger : C.text }}>Revoke</span>
                </div>
              </div>
              <div style={{ position: 'absolute', left: 400, top: 30, transform: `scale(${0.62 * (0.85 + 0.15 * qrShow)})`, transformOrigin: 'top left', opacity: qrShow }}>
                <Img src={staticFile(qr.file)} style={{ width: qr.width, height: qr.height, borderRadius: 12, boxShadow: '0 30px 70px rgba(0,0,0,0.7)' }} />
              </div>
            </div>
          ) : (
            <div style={{ position: 'absolute', left: 0, top: 0, width: 1920, height: 1080, transform: `scale(${1100 / 1920})`, transformOrigin: '0 0' }}>
              <Wallpaper hue={10} />
              <AppWindow kind="notepad" width={1100} height={640} style={{ left: 300, top: 160 }} />
              <div style={{ position: 'absolute', left: 340, top: 230, fontFamily: SANS, fontSize: 34, color: C.text }}>{typed}</div>
              <Cursor path={[{ at: 0, x: 900 + (fx - 200) * 3, y: 540 + (fy - 430) * 2 }]} show={touch > 0 ? 1 : 0.0} scale={1.6} />
              <div style={{ position: 'absolute', left: 960, top: 860, transform: 'translateX(-50%) scale(1.6)', transformOrigin: 'top center' }}>
                <Osd icon={f < audio.cue('output') ? 'speaker_2_24_filled' : 'headphones_sound_wave_24_filled'} color="teal" text={osdText} show={osd} />
              </div>
              <div style={{ position: 'absolute', left: 960, top: 500, transform: `translate(-50%, -50%) scale(${pop(f, micAt + 10) * (1 - ease(f, voicechat.cue('chat') - 6, voicechat.cue('chat')))})`, display: 'flex', alignItems: 'center', gap: 20, padding: '24px 40px', borderRadius: 40, background: 'rgba(30,30,36,0.95)' }}>
                <Fluent name="mic_24_filled" size={50} color={C.teal} />
                <span style={{ fontFamily: SANS, fontSize: 44, fontWeight: 800, color: C.text }}>“open Spotify” → Spotify</span>
              </div>
            </div>
          )}
        </div>
        <div style={{ position: 'absolute', left: 500, top: 640, width: 120, height: 60, background: '#2a2a30' }} />
        <div style={{ position: 'absolute', left: 400, top: 696, width: 320, height: 14, borderRadius: 7, background: '#2a2a30' }} />
      </div>

      {/* The phone */}
      <div style={{ position: 'absolute', left: 1330, top: 90, transform: `translateY(${(1 - phoneIn) * 500}px) rotate(${(1 - phoneIn) * 12 + Math.sin(f / 50) * 1.5}deg) scale(0.88)`, transformOrigin: 'top center', opacity: Math.min(1, phoneIn * 2) }}>
        <Phone width={PW}>
          <PhoneScreens width={PW} screens={screens} />
          {/* Scanning the QR */}
          {scanning > 0 && (
            <div style={{ position: 'absolute', inset: 0, background: `rgba(0,0,0,${0.85 * scanning})` }}>
              <div style={{ position: 'absolute', left: 70, top: 260, width: 260, height: 260, border: `4px solid ${C.accentText}`, borderRadius: 24 }}>
                <div style={{ position: 'absolute', left: 10, right: 10, top: 10 + ((f - scanAt) * 6) % 230, height: 3, background: C.accentText, boxShadow: `0 0 12px ${C.accentText}` }} />
              </div>
              <div style={{ position: 'absolute', left: 0, right: 0, top: 560, textAlign: 'center', fontFamily: SANS, fontSize: 22, color: 'white' }}>MM6F-5FD5</div>
            </div>
          )}
          <Touch x={fx} y={fy} show={touch} />
          <Touch x={120 + 70 * (Math.floor((f - menus.cue('qm')) / 20) % 3)} y={300} show={inOut(f, menus.cue('qm') + 10, menus.cue('qm') + 22, 3)} />
          <Touch x={200} y={250} show={inOut(f, audio.cue('volume') + 2, audio.cue('volume') + 20, 4)} />
          <Touch x={205} y={590} show={inOut(f, audio.cue('music') + 2, audio.cue('music') + 14, 3)} />
        </Phone>
        {/* The «only once» label */}
        <div style={{ position: 'absolute', left: -260, top: 380, transform: `rotate(-6deg) scale(${pop(f, pair.cue('once') - 4)})`, opacity: inOut(f, pair.cue('once') - 4, revoke - 4, 6), padding: '12px 22px', borderRadius: 14, background: C.accentText, fontFamily: SANS, fontSize: 26, fontWeight: 800, color: '#1a1306' }}>
          Works only once
        </div>
      </div>
      <Sfx at={intro.cue('webapp') - 8} name="whoosh" volume={0.25} />
      <Sfx at={scanAt} name="blip" volume={0.2} />
      <Sfx at={paired} name="chime" volume={0.25} />
      <Sfx at={revoke + 10} name="whoosh-down" volume={0.2} />
      {screens.slice(2).map(([at]) => <Sfx key={at} at={at} name="click" volume={0.18} />)}
    </AbsoluteFill>
  );
};

/** Home screen, the internet through a proxy, and the Android app. */
const Anywhere: React.FC<{ pwa: BeatTimes; android: BeatTimes }> = ({ pwa, android }) => {
  const f = useCurrentFrame();
  const homeAt = pwa.cue('home');
  const proxyAt = pwa.cue('proxy');
  const toAndroid = glide(f, android.from - 8, 26);
  const installed = pop(f, homeAt + 10, { damping: 10 });
  const nodes = [
    { x: 380, title: 'Phone', sub: '4G · anywhere', icon: 'phone_24_filled', color: 'sky' },
    { x: 820, title: 'Internet', sub: 'https://kdesktop.example.com', icon: 'globe_24_filled', color: 'blue' },
    { x: 1260, title: 'Reverse proxy', sub: 'Caddy · HTTPS', icon: 'lock_closed_24_filled', color: 'green' },
    { x: 1640, title: 'ALEX-PC', sub: 'web app · port 2502', icon: 'desktop_24_filled', color: 'gold' },
  ];
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ opacity: 1 - toAndroid, transform: `translateX(${-toAndroid * 300}px)` }}>
        {/* Home screen */}
        <div style={{ position: 'absolute', left: 150, top: 160, transform: `scale(0.8) translateY(${(1 - glide(f, pwa.from - 10, 26)) * 300}px)`, transformOrigin: 'top left' }}>
          <Phone width={PW}>
            <div style={{ position: 'absolute', inset: 0, background: 'linear-gradient(160deg,#1c2a5a,#3a1f6a 60%,#0e0e10)' }}>
              <div style={{ position: 'absolute', left: 30, top: 90, display: 'grid', gridTemplateColumns: 'repeat(4, 76px)', gap: '26px 14px' }}>
                {['blue', 'green', 'pink', 'orange', 'teal', 'purple', 'gray', 'red'].map((c, i) => (
                  <div key={i} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6 }}>
                    <KdIcon icon={['mail_24_filled', 'chat_24_filled', 'music_note_2_24_filled', 'camera_24_filled', 'map_24_filled', 'games_24_filled', 'settings_24_filled', 'video_clip_24_filled'][i]} color={c} size={62} />
                    <div style={{ width: 50, height: 6, borderRadius: 3, background: 'rgba(255,255,255,0.3)' }} />
                  </div>
                ))}
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, transform: `scale(${installed})` }}>
                  <Img src={staticFile('brand/icon-192.png')} style={{ width: 62, height: 62, borderRadius: 14, boxShadow: '0 0 30px rgba(242,193,74,0.7)' }} />
                  <span style={{ fontFamily: SANS, fontSize: 13, color: 'white' }}>K-Desktop</span>
                </div>
              </div>
              <div style={{ position: 'absolute', left: 30, right: 30, bottom: 60, padding: 18, borderRadius: 18, background: 'rgba(30,30,36,0.95)', opacity: inOut(f, homeAt - 10, homeAt + 14, 6), fontFamily: SANS, fontSize: 20, color: 'white', display: 'flex', alignItems: 'center', gap: 12 }}>
                <Fluent name="add_circle_24_filled" size={28} color={C.accentText} /> Add to Home screen
              </div>
            </div>
          </Phone>
        </div>
        {/* Through the internet */}
        <div style={{ position: 'absolute', left: 0, top: 0, width: 1920, height: 1080, transform: 'translate(540px, 0) scale(0.7)', transformOrigin: 'left center' }}>
          {nodes.map((n, i) => (
            <React.Fragment key={n.title}>
              {i > 0 && <Wire from={[nodes[i - 1].x + 150, 540]} to={[n.x - 150, 540]} draw={ease(f, proxyAt + i * 8 - 8, proxyAt + i * 8 + 8, OUT)} flow={ease(f, proxyAt + 30, proxyAt + 40)} color={i === 3 ? C.accentText : C.success} width={5} bend={0.3} seed={i} />}
              <Node x={n.x} y={540} icon={n.icon} color={n.color} title={n.title} sub={n.sub} show={pop(f, proxyAt + i * 8 - 10)} lit={i === 2 ? inOut(f, proxyAt + 20, pwa.to, 8) : 0} size={0.95} style={{ flexDirection: 'column', textAlign: 'center' } as React.CSSProperties} />
            </React.Fragment>
          ))}
        </div>
      </AbsoluteFill>
      {/* Android */}
      <AbsoluteFill style={{ opacity: toAndroid, transform: `translateX(${(1 - toAndroid) * 300}px)` }}>
        <div style={{ position: 'absolute', left: 560, top: 90, transform: 'scale(0.86)', transformOrigin: '0 0' }}>
          <Phone width={PW} android>
            <PhoneScreens width={PW} screens={[[0, 'menus'], [android.cue('native') - 4, 'menus']]} />
            <div style={{ position: 'absolute', left: 0, right: 0, bottom: 0, height: 40, background: '#0e0e10', display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 60 }}>
              {['◁', '○', '□'].map(s => <span key={s} style={{ color: '#888', fontSize: 22 }}>{s}</span>)}
            </div>
            <div style={{ position: 'absolute', right: 26, top: 22, width: 46, height: 46, borderRadius: 23, background: C.teal, display: 'flex', alignItems: 'center', justifyContent: 'center', transform: `scale(${1 + 0.25 * inOut(f, android.cue('native') - 4, android.cue('pc') - 4, 6) * Math.abs(Math.sin(f / 5))})` }}>
              <Fluent name="mic_24_filled" size={26} color="#0e0e10" />
            </div>
          </Phone>
        </div>
        <div style={{ position: 'absolute', left: 1120, top: 400, display: 'flex', flexDirection: 'column', gap: 26 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 18, padding: '20px 28px', borderRadius: 22, background: 'rgba(34,34,40,0.96)', border: `1.5px solid ${C.line2}`, opacity: pop(f, android.cue('android') - 6) }}>
            <KdIcon icon="phone_24_filled" color="green" size={60} />
            <div>
              <div style={{ fontFamily: SANS, fontSize: 30, fontWeight: 800, color: C.text }}>K-Desktop for Android</div>
              <Mono size={20} color={C.dim}>KDesktop.apk · Android 5.0+</Mono>
            </div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 18, padding: '20px 28px', borderRadius: 22, background: 'rgba(242,193,74,0.12)', border: `1.5px solid rgba(242,193,74,0.6)`, opacity: pop(f, android.cue('pc') - 6), transform: `translateX(${(1 - pop(f, android.cue('pc') - 6)) * 60}px)` }}>
            <Fluent name="arrow_sync_24_filled" size={44} color={C.accentText} />
            <div>
              <div style={{ fontFamily: SANS, fontSize: 26, fontWeight: 800, color: C.text }}>Updated by your PC</div>
              <div style={{ fontFamily: SANS, fontSize: 19, color: C.dim }}>No store needed</div>
            </div>
          </div>
        </div>
      </AbsoluteFill>
      <Sfx at={homeAt + 10} name="pop" volume={0.3} />
      <Sfx at={proxyAt} name="whoosh-short" volume={0.2} />
      <Sfx at={android.from - 8} name="whoosh" volume={0.22} />
      <Sfx at={android.cue('pc') - 6} name="chime" volume={0.2} />
    </AbsoluteFill>
  );
};

type Line = { at: number; text: string; kind: 'cmd' | 'out' | 'dim' };

/** A terminal that types its commands and prints the answers. */
const Terminal: React.FC<{ lines: Line[]; title: string; width?: number; height?: number }> = ({ lines, title, width = 1000, height = 640 }) => {
  const f = useCurrentFrame();
  const shown = lines.filter(l => f >= l.at);
  return (
    <div style={{ width, height, borderRadius: 16, overflow: 'hidden', background: '#0c0c10', border: `1px solid ${C.line2}`, boxShadow: '0 40px 90px rgba(0,0,0,0.6)' }}>
      <div style={{ height: 44, display: 'flex', alignItems: 'center', gap: 10, padding: '0 16px', background: '#1a1a20', fontFamily: SANS, fontSize: 16, color: C.dim }}>
        <Fluent name="window_console_20_filled" size={18} color={C.dim} /> {title}
      </div>
      <div style={{ padding: '16px 22px', fontFamily: MONO, fontSize: 21, lineHeight: 1.5, display: 'flex', flexDirection: 'column', justifyContent: 'flex-end', height: height - 76, overflow: 'hidden' }}>
        {shown.map((l, i) => {
          const typing = l.kind === 'cmd' ? l.text.slice(0, Math.max(0, Math.floor((f - l.at) * 1.6))) : l.text;
          return (
            <div key={i} style={{ whiteSpace: 'pre', color: l.kind === 'cmd' ? C.text : l.kind === 'dim' ? C.faint : C.teal }}>
              {l.kind === 'cmd' && <span style={{ color: C.accentText }}>$ </span>}{typing}
            </div>
          );
        })}
      </div>
    </div>
  );
};

const Machines: React.FC<{ udp: BeatTimes; cli: BeatTimes }> = ({ udp, cli }) => {
  const f = useCurrentFrame();
  const nc = (s: string) => `echo "${s}" | nc -u -w1 alex-pc $PORT`;
  const lines: Line[] = [
    { at: udp.cue('list') - 6, text: nc('list'), kind: 'cmd' },
    { at: udp.cue('list') + 30, text: '[3] - list (done)', kind: 'out' },
    { at: udp.cue('list') + 32, text: '-------[K-DESKTOP 5.0] - [ALEX-PC]-------', kind: 'dim' },
    { at: udp.cue('list') + 34, text: '[1] - Play/Pause   [2] - Next   [3] - Previous …', kind: 'dim' },
    { at: udp.cue('list') + 36, text: '[20] - Gaming mode', kind: 'out' },
    { at: udp.cue('list') + 37, text: '[21] - Living room lights off', kind: 'out' },
    { at: udp.cue('list') + 38, text: '[22] - Work mode', kind: 'out' },
    { at: udp.cue('number') - 6, text: nc('22'), kind: 'cmd' },
    { at: udp.cue('number') + 30, text: '[22] - Work mode (done)', kind: 'out' },
    { at: udp.cue('sk') - 6, text: nc('sk ctrl+s'), kind: 'cmd' },
    { at: udp.cue('sk') + 30, text: 'SendKey [ctrl+s] (done)', kind: 'out' },
    { at: udp.cue('event') - 6, text: nc('event Doorbell'), kind: 'cmd' },
    { at: udp.cue('event') + 30, text: 'Evento [Doorbell] (done)', kind: 'out' },
  ];
  const cliLines: Line[] = [
    { at: cli.cue('run') - 4, text: 'KDesktop.exe --run "Work mode"', kind: 'cmd' },
    { at: cli.cue('menu') - 4, text: 'KDesktop.exe --menu "Main"', kind: 'cmd' },
    { at: cli.cue('fire') - 4, text: 'KDesktop.exe --event "Backup finished"', kind: 'cmd' },
    { at: cli.cue('launcher') - 4, text: 'KDesktop.exe --launcher', kind: 'cmd' },
  ];
  const toCli = glide(f, cli.from - 8, 24);
  const senders = [
    { y: 300, title: 'Home Assistant', icon: 'home_24_filled', color: 'sky' },
    { y: 540, title: 'Scripts', icon: 'code_24_filled', color: 'dark' },
    { y: 780, title: 'Another PC', icon: 'desktop_24_filled', color: 'blue' },
  ];
  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ opacity: 1 - toCli }}>
        {senders.map((s, i) => (
          <React.Fragment key={s.title}>
            <Wire from={[430, s.y]} to={[760, 540]} draw={ease(f, udp.cue('ha') - 10 + i * 4, udp.cue('ha') + 6 + i * 4, OUT)} flow={1} color={C.accentText} seed={i} />
            <Node x={270} y={s.y} icon={s.icon} color={s.color} title={s.title} show={pop(f, udp.cue('ha') - 12 + i * 4)} size={0.9} />
          </React.Fragment>
        ))}
        <div style={{ position: 'absolute', left: 140, top: 140, display: 'flex', alignItems: 'center', gap: 18, opacity: pop(f, udp.cue('udp') - 6) }}>
          <KdIcon icon="plug_connected_24_filled" color="pink" size={70} />
          <div>
            <div style={{ fontFamily: SANS, fontSize: 40, fontWeight: 800, color: C.text }}>UDP</div>
            <div style={{ fontFamily: SANS, fontSize: 22, color: C.dim }}>Compatible with K-Desktop 4</div>
          </div>
        </div>
        <div style={{ position: 'absolute', left: 780, top: 230, transform: `translateX(${(1 - glide(f, udp.from - 6, 24)) * 200}px)` }}>
          <Terminal lines={lines} title="bash — alex@nas" width={1040} height={660} />
        </div>
      </AbsoluteFill>
      <AbsoluteFill style={{ opacity: toCli, transform: `scale(${0.9 + 0.1 * toCli})` }}>
        <div style={{ position: 'absolute', left: 960, top: 540, transform: 'translate(-50%, -50%)' }}>
          <Terminal lines={cliLines} title="Windows Terminal" width={1200} height={420} />
        </div>
      </AbsoluteFill>
      {lines.filter(l => l.kind === 'cmd').map(l => <Sfx key={l.at} at={l.at} name="key" volume={0.2} />)}
      {cliLines.map(l => <Sfx key={l.at} at={l.at} name="key" volume={0.2} />)}
    </AbsoluteFill>
  );
};

export const Remote: React.FC = () => {
  const B = useBeats();
  const { durationInFrames } = useChapter();
  const pwa = B('pwa');
  const udp = B('udp');
  return (
    <AbsoluteFill>
      <Part from={0} to={pwa.from - 6} first><PhonePart intro={B('intro')} pair={B('pair')} menus={B('menus')} pad={B('pad')} audio={B('audio')} voicechat={B('voicechat')} /></Part>
      <Part from={pwa.from - 6} to={udp.from - 6}><Anywhere pwa={pwa} android={B('android')} /></Part>
      <Part from={udp.from - 6} to={durationInFrames} last><Machines udp={udp} cli={B('cli')} /></Part>
    </AbsoluteFill>
  );
};

