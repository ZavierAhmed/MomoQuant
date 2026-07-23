import { Navigate, Route, Routes } from 'react-router-dom';
import { RoleGuard } from '@/auth/RoleGuard';
import { ProtectedRoute } from '@/auth/ProtectedRoute';
import { AppLayout } from '@/components/layout/AppLayout';
import { AdminUsersPage } from '@/pages/AdminUsersPage';
import { AiAnalyticsPage } from '@/pages/AiAnalyticsPage';
import { BacktestDetailPage } from '@/pages/BacktestDetailPage';
import { BacktestingPage } from '@/pages/BacktestingPage';
import { BotControlPage } from '@/pages/BotControlPage';
import { DashboardPage } from '@/pages/DashboardPage';
import { ExchangesSymbolsPage } from '@/pages/ExchangesSymbolsPage';
import { LogsPage } from '@/pages/LogsPage';
import { MarketWatchPage } from '@/pages/MarketWatchPage';
import { MonitoringPage } from '@/pages/MonitoringPage';
import { NotFoundPage } from '@/pages/NotFoundPage';
import { OrdersPage } from '@/pages/OrdersPage';
import { PaperAccountDetailPage } from '@/pages/PaperAccountDetailPage';
import { PaperSessionDetailPage } from '@/pages/PaperSessionDetailPage';
import { PaperTradingPage } from '@/pages/PaperTradingPage';
import { PositionsPage } from '@/pages/PositionsPage';
import { ReplayDetailPage } from '@/pages/ReplayDetailPage';
import { ReplayPage } from '@/pages/ReplayPage';
import { ReportsPage } from '@/pages/ReportsPage';
import { RiskManagementPage } from '@/pages/RiskManagementPage';
import { SettingsPage } from '@/pages/SettingsPage';
import { SignInPage } from '@/pages/SignInPage';
import { SkAnalysisDetailPage } from '@/pages/SkAnalysisDetailPage';
import { SkLivePaperPage } from '@/pages/SkLivePaperPage';
import { SkSystemAnalyzerPage } from '@/pages/SkSystemAnalyzerPage';
import { TradingSystemsPage } from '@/pages/TradingSystemsPage';
import { StrategiesPage } from '@/pages/StrategiesPage';
import { StrategyDetailPage } from '@/pages/StrategyDetailPage';
import { StrategyBenchmarkDetailPage } from '@/pages/StrategyBenchmarkDetailPage';
import { StrategyBenchmarksPage } from '@/pages/StrategyBenchmarksPage';
import { StrategyLabPage } from '@/pages/StrategyLabPage';
import { StrategyLabRunDetailPage } from '@/pages/StrategyLabRunDetailPage';
import { StrategyLabRunsPage } from '@/pages/StrategyLabRunsPage';
import { SystemCleanupPage } from '@/pages/SystemCleanupPage';
import { TradingSettingsPage } from '@/pages/TradingSettingsPage';
import { TradesPage } from '@/pages/TradesPage';
import { ValidationLabExperimentDetailPage } from '@/pages/ValidationLabExperimentDetailPage';
import { ValidationLabNewPage } from '@/pages/ValidationLabNewPage';
import { ValidationLabPage } from '@/pages/ValidationLabPage';

export function AppRouter() {
  return (
    <Routes>
      <Route path="/signin" element={<SignInPage />} />

      <Route element={<ProtectedRoute />}>
        <Route element={<AppLayout />}>
          <Route index element={<Navigate to="/dashboard" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route
            path="/bot-control"
            element={
              <RoleGuard allowedRoles={['Admin', 'Trader']}>
                <BotControlPage />
              </RoleGuard>
            }
          />
          <Route path="/market-watch" element={<MarketWatchPage />} />
          <Route path="/exchanges-symbols" element={<ExchangesSymbolsPage />} />
          <Route path="/strategies" element={<StrategiesPage />} />
          <Route path="/strategies/:strategyCode" element={<StrategyDetailPage />} />
          <Route path="/backtesting" element={<BacktestingPage />} />
          <Route path="/backtesting/:id" element={<BacktestDetailPage />} />
          <Route path="/strategy-benchmarks" element={<StrategyBenchmarksPage />} />
          <Route path="/strategy-benchmarks/:id" element={<StrategyBenchmarkDetailPage />} />
          <Route path="/strategy-lab" element={<StrategyLabPage />} />
          <Route path="/strategy-lab/runs" element={<StrategyLabRunsPage />} />
          <Route path="/strategy-lab/runs/:runId" element={<StrategyLabRunDetailPage />} />
          <Route path="/validation-lab" element={<ValidationLabPage />} />
          <Route path="/validation-lab/new" element={<ValidationLabNewPage />} />
          <Route path="/validation-lab/experiments/:experimentId" element={<ValidationLabExperimentDetailPage />} />
          <Route path="/replay" element={<ReplayPage />} />
          <Route path="/replay/:id" element={<ReplayDetailPage />} />
          <Route path="/trading-systems" element={<TradingSystemsPage />} />
          <Route path="/trading-systems/sk-livepaper" element={<SkLivePaperPage />} />
          <Route path="/trading-systems/sk" element={<SkSystemAnalyzerPage />} />
          <Route path="/trading-systems/sk-system" element={<SkSystemAnalyzerPage />} />
          <Route path="/trading-systems/sk/analyses" element={<SkSystemAnalyzerPage />} />
          <Route path="/trading-systems/sk-system/analyses" element={<SkSystemAnalyzerPage />} />
          <Route path="/trading-systems/sk/analyses/:id" element={<SkAnalysisDetailPage />} />
          <Route path="/trading-systems/sk-system/analyses/:id" element={<SkAnalysisDetailPage />} />
          <Route path="/paper-trading" element={<PaperTradingPage />} />
          <Route path="/paper-trading/accounts/:id" element={<PaperAccountDetailPage />} />
          <Route path="/paper-trading/sessions/:id" element={<PaperSessionDetailPage />} />
          <Route path="/trades" element={<TradesPage />} />
          <Route path="/orders" element={<OrdersPage />} />
          <Route path="/positions" element={<PositionsPage />} />
          <Route path="/reports" element={<ReportsPage />} />
          <Route path="/ai-analytics" element={<AiAnalyticsPage />} />
          <Route path="/risk-management" element={<RiskManagementPage />} />
          <Route path="/monitoring" element={<MonitoringPage />} />
          <Route path="/logs" element={<LogsPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/settings/trading" element={<TradingSettingsPage />} />
          <Route
            path="/admin/users"
            element={
              <RoleGuard allowedRoles={['Admin']}>
                <AdminUsersPage />
              </RoleGuard>
            }
          />
          <Route
            path="/admin/system-cleanup"
            element={
              <RoleGuard allowedRoles={['Admin']}>
                <SystemCleanupPage />
              </RoleGuard>
            }
          />
        </Route>
      </Route>

      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}
