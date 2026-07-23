import { NavLink } from 'react-router-dom';
import { getNavSections } from '@/app/navigation';
import { useAuth } from '@/auth/useAuth';

export function Sidebar() {
  const { user } = useAuth();
  const sections = getNavSections(user?.role ?? null);

  return (
    <aside className="flex h-full w-64 shrink-0 flex-col border-r border-slate-800 bg-slate-950">
      <div className="border-b border-slate-800 px-5 py-4">
        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">MOMO Quant</p>
        <p className="mt-1 text-sm font-medium text-slate-200">Trading Dashboard</p>
      </div>

      <nav className="flex-1 overflow-y-auto px-3 py-4">
        {sections.map((section) => (
          <div key={section.title} className="mb-5">
            <p className="px-3 pb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
              {section.title}
            </p>
            <ul className="space-y-1">
              {section.items.map((item) => (
                <li key={item.path}>
                  <NavLink
                    to={item.path}
                    className={({ isActive }) =>
                      [
                        'block rounded-lg px-3 py-2 text-sm transition-colors',
                        isActive
                          ? 'bg-slate-800 text-white'
                          : 'text-slate-300 hover:bg-slate-900 hover:text-white',
                      ].join(' ')
                    }
                  >
                    {item.label}
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>
    </aside>
  );
}
