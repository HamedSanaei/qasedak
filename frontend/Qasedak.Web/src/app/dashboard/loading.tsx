import { Card, Skeleton } from "@/shared/design/Feedback";
import styles from "@/shared/design/RouteState.module.css";

export default function DashboardLoading() {
  return <div className={styles.loading} aria-label="در حال بارگذاری داشبورد" aria-busy="true"><Skeleton width="180px" height={34} /><Card className={styles.loadingCard}><Skeleton width="56%" height={26} /><Skeleton width="84%" height={14} /><span className={styles.spacerSmall} /><Skeleton width="220px" height={48} /></Card></div>;
}
