import styles from './App.module.css';
import { AppShell } from './components/AppShell';
import { useHashRoute } from './router/hashRouter';
import { RequestDetail } from './views/RequestDetail';
import { RequestList } from './views/RequestList';

export default function App() {
  const route = useHashRoute();
  return (
    <AppShell>
      {route.name === 'list' && <RequestList />}
      {route.name === 'detail' && <RequestDetail id={route.id} />}
      {route.name === 'not-found' && (
        <div className={styles.notFound}>
          <a href="#/">Back to requests</a>
        </div>
      )}
    </AppShell>
  );
}
