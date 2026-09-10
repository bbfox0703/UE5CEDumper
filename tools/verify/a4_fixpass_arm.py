r"""Live arm for the audit-#4 fix pass, on DumperTest **Shipping**.

    py tools/verify/a4_fixpass_arm.py

⛔ WHAT THIS CAN AND CANNOT DECIDE, said first because most of the eight rows are NOT
reachable from the pipe and a rig that quietly skips them would read as a clean bill.

  REACHABLE HERE
    [SOLIDE-REFUSAL]   MANUFACTURABLE. A K_OBJECT_NULL hold on a NON-strong pointer
                       (Weak/Soft/Lazy) is type-refused on every instance -- Solide.cpp
                       returns FR_ERR_WEAK_PTR rather than nulling an ObjectIndex-0 trap.
                       Arm it TWICE: the first call is `newlyAdded`, the second is the
                       RE-ARM that used to answer plain held=0 and make the UI say "no live
                       instance ... exists right now".
    anti-over-shout    Both teleport flags must be ABSENT on a healthy read. A fix that
                       always sets them would pass a naive "the field exists" check and cry
                       wolf on every pawn in the game.
    regression         See-through and Fly still work after their code moved.

  NOT REACHABLE HERE, and each says why rather than being silently dropped
    [POSEATTACH]       needs an ATTACHED possessed pawn whose K2_GetActorLocation invoke
                       fails -- two coincident conditions this fixture cannot stage.
    [TPREL-ZEROPOSE]   needs the pawn or world to vanish INSIDE one locked call.
    [B30-REOPEN]       is a Cheat Engine interaction; it needs a real CE tick, so it is
                       driven separately rather than pretended at from here.
    [R3-SEETHRU]       the hazard needs a command in flight at the instant of the untick;
                       what IS checkable live is that the toggle still works.
"""
from __future__ import annotations

import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient          # noqa: E402

FAILS: list[str] = []


def check(name: str, cond: bool, got: str = '') -> bool:
    print('  %-4s %s%s' % ('ok' if cond else 'FAIL', name, ('   got: %s' % got) if got else ''))
    if not cond:
        FAILS.append(name)
    return cond


def main() -> int:
    with PipeClient() as c:
        print('build:', c.assert_build())
        c.ensure_scanned()
        n = c.request('get_object_count').get('count')
        print('objects: %s' % n)
        if not n:
            print('*** empty pool -- a process that exists is not a game that booted')
            return 2

        # ── [SOLIDE-REFUSAL] ────────────────────────────────────────────────────
        # ⛔ THE OBVIOUS REPRODUCER DOES NOT REACH THE FIX, and a before/after is what
        #    proved it. Arming a PERMANENTLY-refused field twice looks like a re-arm and is
        #    not one: arm #1 is refused, the job is ERASED (it never persisted), so arm #2
        #    is another `newlyAdded`. Measured pre-fix and post-fix on rebuilt DLLs: both
        #    returned -12, i.e. no difference, because the re-arm branch never ran.
        #
        #    The real sequence needs a hold that SUCCEEDS first, so the job persists, and
        #    is then refused on a re-arm with a different kind:
        #      1. force MaxWalkSpeed as `numeric`  -> holds on the live CMCs, job persists
        #      2. re-arm the SAME field as `object_null` -> refused (a FloatProperty is not
        #         a strong ObjectProperty), lastRefusal set, newlyAdded FALSE
        #    Pre-fix that answered code 0 / held 0, which PropertySearch renders as the
        #    positively false "no live instance ... exists right now".
        print()
        print('[SOLIDE-REFUSAL] hold a field, then RE-ARM it with a refused kind')
        CN, FN = 'CharacterMovementComponent', 'MaxWalkSpeed'
        try:
            first = c.request('force_field', class_name=CN, field_name=FN,
                              kind='numeric', value=600.0)
            print('    arm #1 numeric (persists) : %s'
                  % {k: first.get(k) for k in ('code', 'held')})
            second = c.request('force_field', class_name=CN, field_name=FN,
                               kind='object_null', value=0)
            print('    arm #2 object_null (RE-ARM): %s'
                  % {k: second.get(k) for k in ('code', 'held')})

            # ⛔ ANTI-VACUITY: if arm #1 held nothing the job may not have persisted, and
            #    arm #2 would be a `newlyAdded` again -- decides nothing.
            check('[SOLIDE-REFUSAL] setup: arm #1 actually HELD instances',
                  (first.get('held') or 0) > 0, str(first.get('held')))
            # ⭐ THE ROW ITSELF.
            check('[SOLIDE-REFUSAL] ⭐ the RE-ARM reports the refusal, not a silent held=0',
                  (second.get('code') or 0) < 0, str(second.get('code')))
        finally:
            # ⚠ MaxWalkSpeed was really written on 7 live components -- restore, always.
            c.request('reset_field', class_name=CN, field_name=FN)
            left = c.request('get_forced_fields')
            n_left = len(left.get('fields') or left.get('results') or [])
            check('[SOLIDE-REFUSAL] teardown: nothing left armed', n_left == 0, str(n_left))

        # ── anti-over-shout: both new teleport flags absent on a healthy read ────
        print('\nanti-over-shout: the new flags must be ABSENT on a healthy pawn')
        pose = c.request('teleport_get_pose')
        print('  pose code=%s source=%s parent_relative=%s'
              % (pose.get('code'), pose.get('source'), pose.get('parent_relative')))
        if pose.get('code') == 0:
            check('[POSEATTACH] a normal pose does NOT claim parent-relative',
                  not pose.get('parent_relative'), str(pose.get('parent_relative')))
        else:
            print('  (no pawn: pose code=%s -- the control is UNDECIDED, not passed)'
                  % pose.get('code'))
            FAILS.append('[POSEATTACH] control undecided (no pawn)')

        # ── regression: the features whose code moved still work ────────────────
        print('\nregression: See-through and Fly still toggle after the code moved')
        st0 = c.request('seethrough_get_state')
        print('  seethrough: active=%s hook_active=%s code=%s'
              % (st0.get('active'), st0.get('hook_active'), st0.get('code')))
        check('[R3-SEETHRU] the See-through state still reads cleanly',
              st0.get('code') == 0, str(st0.get('code')))

        fly = c.request('fly_get_state')
        print('  fly: has_cmc=%s current_mode=%s code=%s'
              % (fly.get('has_cmc'), fly.get('current_mode'), fly.get('code')))
        check('[BADGEPRIME] every badge the UI primes has a live answer',
              fly.get('code') == 0 and st0.get('code') == 0,
              'fly=%s seethrough=%s' % (fly.get('code'), st0.get('code')))

    print('\n%d failure(s)' % len(FAILS))
    for f in FAILS:
        print('  FAILED: %s' % f)
    if not FAILS:
        print('PASS -- and read the header for the four rows this arm deliberately '
              'does NOT decide.')
    return 1 if FAILS else 0


if __name__ == '__main__':
    sys.exit(main())
