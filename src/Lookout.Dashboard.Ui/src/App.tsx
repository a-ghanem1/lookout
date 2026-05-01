import styles from './App.module.css';
import { AppShell } from './components/AppShell';
import { useHashRoute } from './router/hashRouter';
import { CachePage } from './pages/Cache/CachePage';
import { DumpPage } from './pages/Dump/DumpPage';
import { ExceptionsPage } from './pages/Exceptions/ExceptionsPage';
import { HttpClientsPage } from './pages/HttpClients/HttpClientsPage';
import { JobsPage } from './pages/Jobs/JobsPage';
import { LogsPage } from './pages/Logs/LogsPage';
import { QueriesPage } from './pages/Queries/QueriesPage';
import { JobPage } from './views/JobPage';
import { RequestDetail } from './views/RequestDetail';
import { RequestList } from './views/RequestList';

export default function App() {
  const route = useHashRoute();

  return (
    <AppShell route={route}>
      {route.name === 'list' && <RequestList />}
      {route.name === 'detail' && <RequestDetail key={route.id} id={route.id} />}
      {route.name === 'job' && <JobPage id={route.id} />}
      {route.name === 'queries' && <QueriesPage />}
      {route.name === 'query-detail' && <QueriesPage id={route.id} />}
      {route.name === 'exceptions' && <ExceptionsPage />}
      {route.name === 'exception-detail' && <ExceptionsPage id={route.id} />}
      {route.name === 'logs' && <LogsPage />}
      {route.name === 'cache' && <CachePage />}
      {route.name === 'http-clients' && <HttpClientsPage />}
      {route.name === 'http-client-detail' && <HttpClientsPage id={route.id} />}
      {route.name === 'jobs' && <JobsPage />}
      {route.name === 'dump' && <DumpPage />}
      {route.name === 'not-found' && (
        <div className={styles.notFound}>
          <p>Page not found.</p>
          <a href="#/requests">← Back to requests</a>
        </div>
      )}
    </AppShell>
  );
}
