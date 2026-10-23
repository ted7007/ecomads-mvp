export function BrandLogo({ white = false, height = 34 }: { white?: boolean; height?: number }) {
  return <img src={`/brand/logo-horizontal-${white ? 'mono-white' : 'color'}.svg`} alt="EcomAds"
    height={height} style={{ display: 'block', width: 'auto', maxWidth: '100%', flexShrink: 0 }} />;
}
