-- Migration 5: the audit chain must follow the order entries are committed.
--
-- Migration 1 chained each entry to "the entry with the highest id" and serialised writers with an advisory lock. But an entry's id
-- is drawn BEFORE the trigger runs, so two writers at the same moment can draw ids 5 and 6, and writer 6 can take the lock first.
-- Entry 6 then chains to 4, entry 5 chains to 6, and verify_access_log() (which reads entries in id order) reports a break that no
-- one caused. With many phones syncing together, and now every sign-in being logged, that happens for real.
--
-- The fix: draw the id again once the lock is held. From then on id order is lock order is commit order (the lock lasts to the end
-- of the transaction), and the chain is exactly what verify_access_log() expects. The hash itself is unchanged.
-- Entries written before this migration under concurrent load, if any, keep their break: the log is append-only on purpose.

CREATE OR REPLACE FUNCTION access_log_chain() RETURNS trigger AS $$
BEGIN
  PERFORM pg_advisory_xact_lock(843);
  NEW.id := nextval(pg_get_serial_sequence('access_log', 'id'));
  SELECT entry_hash INTO NEW.prev_hash FROM access_log ORDER BY id DESC LIMIT 1;
  NEW.entry_hash := encode(digest(
    coalesce(NEW.prev_hash, '') || '|' || NEW.accessed_by || '|' || coalesce(NEW.role, '') || '|' ||
    coalesce(NEW.patient_id::text, '') || '|' || NEW.action || '|' || NEW.outcome || '|' ||
    NEW.purpose_of_use || '|' || NEW.accessed_at::text, 'sha256'), 'hex');
  RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;
