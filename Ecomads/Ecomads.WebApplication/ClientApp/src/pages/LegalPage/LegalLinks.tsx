import { Link } from 'react-router-dom';
export const legalTitles = {'privacy': 'Политика конфиденциальности', 'consent': 'Согласие на обработку персональных данных', 'cookies': 'Политика использования cookie'} as const;
export function LegalLinks() {
  return <nav aria-label="Документы" style={{ display: 'flex', flexWrap: 'wrap', gap: '8px 16px' }}>
    {Object.entries(legalTitles).map(([slug, title]) =>
      <Link key={slug} to={'/legal/' + slug} target="_blank" rel="noopener noreferrer">{title}</Link>)}
  </nav>;
}
