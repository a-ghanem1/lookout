import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import styles from './index.module.css';

const features = [
  {
    title: 'Zero config',
    body: 'Three lines in Program.cs. No database setup, no separate process, no config file. Your first capture appears the moment you hit an endpoint.',
  },
  {
    title: 'N+1 detection',
    body: 'Automatically flags 3+ identical SQL shapes per request and groups them in a banner with a stack trace pointing to the line in your code.',
  },
  {
    title: 'Full-request correlation',
    body: 'EF queries, outbound HTTP, cache hits/misses, exceptions, ILogger output, and Hangfire jobs — all linked to the request that caused them.',
  },
  {
    title: 'Full-text search',
    body: 'Press Cmd+K to search across every captured entry — SQL, log messages, exception types, URLs, and custom tags.',
  },
  {
    title: 'Keyboard-first',
    body: 'Navigate the request list with j/k, open with Enter, go back with Esc. No mouse required once the dashboard loads.',
  },
  {
    title: 'Safe by default',
    body: 'Throws at startup in Production unless explicitly opted in. Warns when bound to a non-loopback address. CSRF-protected mutating endpoints.',
  },
];

export default function Home() {
  const { siteConfig } = useDocusaurusContext();
  return (
    <Layout description={siteConfig.tagline}>
      <main>
        <section className={styles.hero}>
          <img src="img/logo.png" alt="Lookout" className={styles.heroLogo} />
          <h1 className={styles.heroTitle}>{siteConfig.title}</h1>
          <p className={styles.heroSubtitle}>{siteConfig.tagline}</p>

          <div className={styles.install}>
            <code>dotnet add package Lookout.AspNetCore</code>
          </div>

          <div className={styles.cta}>
            <Link
              className="button button--primary button--lg"
              to="/docs/quickstart"
            >
              Get started in 2 minutes
            </Link>
            <Link
              className="button button--secondary button--lg"
              href="https://github.com/a-ghanem1/Lookout"
            >
              View on GitHub
            </Link>
          </div>
        </section>

        <section className={styles.features}>
          {features.map((f) => (
            <div key={f.title} className={styles.feature}>
              <h3 className={styles.featureHeading}>{f.title}</h3>
              <p>{f.body}</p>
            </div>
          ))}
        </section>
      </main>
    </Layout>
  );
}
