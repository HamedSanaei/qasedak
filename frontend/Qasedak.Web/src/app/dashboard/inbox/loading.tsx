import { Card, Skeleton } from "@/shared/design/Feedback";
import styles from "@/shared/design/RouteState.module.css";

export default function InboxLoading() {
  return <div className={styles.loading} aria-label="در حال بارگذاری گفتگوها" aria-busy="true"><Skeleton width="190px" height={34} /><Card className={styles.loadingCard}><Skeleton width="45%" height={18} /><Skeleton height={78} /><Skeleton height={78} /><Skeleton height={78} /></Card></div>;
}
