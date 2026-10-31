import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Fluent, KdIcon } from '../../../src/components/Icon';
import { Sfx } from '../../../src/components/Sfx';
import { glide, pop } from '../../../src/lib/anim';
import { t } from '../../../src/lib/i18n';
import { useBeats } from '../../../src/lib/timeline';
import { C, SANS } from '../../../src/theme';

type Row = { label: string; value?: string; cue: string };
type Act = { name: string; detail: string; icon: string; color: string; cue: string };

/** One event, drawn with the editor's real labels: its trigger, its settings and what it runs. */
const EventCard: React.FC<{ x: number; at: number; icon: string; color: string; name: string; rows: Row[]; acts: Act[]; cue: (c: string) => number }> = ({ x, at, icon, color, name, rows, acts, cue }) => {
  const f = useCurrentFrame();
  const k = glide(f, at, 22);
  return (
    <div style={{
      position: 'absolute', left: x, top: 270, width: 800, padding: '28px 32px', borderRadius: 26, boxSizing: 'border-box', fontFamily: SANS,
      background: 'linear-gradient(180deg, rgba(44,44,52,0.97), rgba(30,30,36,0.97))', border: `1.5px solid ${C.line2}`, boxShadow: '0 30px 80px rgba(0,0,0,0.55)',
      opacity: k, transform: `translateY(${(1 - k) * 60}px)`,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 18, marginBottom: 22 }}>
        <KdIcon icon={icon} color={color} size={60} />
        <span style={{ fontSize: 32, fontWeight: 700, color: C.text }}>{name}</span>
      </div>
      {rows.map(r => {
        const p = pop(f, cue(r.cue) - 6, { damping: 14 });
        return (
          <div key={r.label} style={{ display: 'flex', alignItems: 'center', gap: 14, height: 48, fontSize: 22, color: C.text, opacity: Math.min(1, p * 1.4), transform: `translateX(${(1 - p) * -30}px)` }}>
            {r.value === undefined ? <Fluent name="checkbox_checked_24_filled" size={26} color={C.accentText} /> : null}
            <span style={{ color: r.value === undefined ? C.text : C.dim }}>{r.label}</span>
            {r.value !== undefined && <span style={{ marginLeft: 'auto', padding: '6px 14px', borderRadius: 8, background: 'rgba(255,255,255,0.07)', fontWeight: 600 }}>{r.value}</span>}
          </div>
        );
      })}
      <div style={{ height: 1, background: C.line2, margin: '16px 0 18px' }} />
      {acts.map(a => {
        const p = pop(f, cue(a.cue) - 6, { damping: 14 });
        return (
          <div key={a.name} style={{
            display: 'flex', alignItems: 'center', gap: 16, height: 66, padding: '0 18px', borderRadius: 14, marginBottom: 10, background: 'rgba(255,255,255,0.05)',
            opacity: Math.min(1, p * 1.4), transform: `scale(${0.9 + 0.1 * p})`,
          }}>
            <KdIcon icon={a.icon} color={a.color} size={42} />
            <span style={{ fontSize: 24, fontWeight: 600, color: C.text }}>{a.name}</span>
            <span style={{ marginLeft: 'auto', fontSize: 20, color: C.dim }}>{a.detail}</span>
          </div>
        );
      })}
    </div>
  );
};

/** Chapter «One more thing»: a calendar event and a mail event, side by side. */
export const Tip: React.FC = () => {
  const B = useBeats();
  const tip = B('tip');
  return (
    <AbsoluteFill>
      <EventCard x={140} at={tip.cue('before') - 14} icon="calendar_ltr_24_filled" color="orange" name={t('Calendar event')} cue={tip.cue}
        rows={[{ label: t('Minutes before'), value: '10', cue: 'before' }, { label: t('Online meetings only (Teams, Meet, Zoom…)'), cue: 'before' }]}
        acts={[
          { name: t('Media'), detail: t('Pause'), icon: 'play_circle_24_filled', color: 'pink', cue: 'dnd' },
          { name: t('Run application'), detail: t('Microsoft Teams'), icon: 'open_24_filled', color: 'blue', cue: 'teams' },
        ]} />
      <EventCard x={980} at={tip.cue('bill') - 14} icon="mail_24_filled" color="blue" name={t('Mail arrives')} cue={tip.cue}
        rows={[{ label: t('Subject contains'), value: t('electricity'), cue: 'bill' }, { label: t('Save its attachments in (optional)'), value: t('Documents\Bills'), cue: 'save' }, { label: t('Which attachments (*.pdf; empty: all)'), value: '*.pdf', cue: 'save' }]}
        acts={[{ name: t('Show message'), detail: t('The bill is in Bills'), icon: 'chat_24_filled', color: 'blue', cue: 'save' }]} />
      <Sfx at={tip.cue('before') - 14} name="whoosh-short" volume={0.22} />
      <Sfx at={tip.cue('dnd') - 6} name="pop" volume={0.2} />
      <Sfx at={tip.cue('teams') - 6} name="pop" volume={0.2} />
      <Sfx at={tip.cue('bill') - 14} name="whoosh-short" volume={0.22} />
      <Sfx at={tip.cue('save') - 6} name="pop" volume={0.2} />
    </AbsoluteFill>
  );
};
