import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('budget', Path(__file__).with_name('request-budget.py'))
model = importlib.util.module_from_spec(spec)
spec.loader.exec_module(model)


class BudgetTests(unittest.TestCase):
    def test_independent_sellers_progress_without_sharing_quota(self):
        slots = model.Slots()
        self.assertTrue(slots.reserve('seller1', 'fullstats', 0, 3600))
        self.assertTrue(slots.reserve('seller2', 'fullstats', 0, 3600))
        self.assertFalse(slots.reserve('seller1', 'fullstats', 3599, 3600))
        self.assertTrue(slots.reserve('seller1', 'fullstats', 3600, 3600))

    def test_same_seller_alias_does_not_create_an_extra_quota(self):
        slots = model.Slots()
        self.assertTrue(slots.reserve('same-seller', 'products', 0, 1800))
        self.assertFalse(slots.reserve('same-seller', 'products', 0, 1800))
        self.assertTrue(slots.reserve('same-seller', 'fullstats', 0, 3600))

    def test_429_deferral_does_not_block_another_method(self):
        slots = model.Slots()
        slots.reserve('seller', 'products', 0, 1800)
        slots.retry_after('seller', 'products', 0, 7200)
        self.assertFalse(slots.reserve('seller', 'products', 3600, 1800))
        self.assertTrue(slots.reserve('seller', 'fullstats', 3600, 3600))
        self.assertTrue(slots.reserve('seller', 'products', 7200, 1800))

    def test_pagination_can_exhaust_history_capacity(self):
        self.assertGreater(model.budget(100)['products_slot']['spare_requests'], 0)
        self.assertEqual(0, model.budget(100, pages=3)['products_slot']['spare_requests'])
        self.assertIsNone(model.budget(100, pages=3)['products_slot']['optimistic_history_days'])

    def test_100_campaigns_need_two_fullstats_batches(self):
        self.assertEqual(6, model.budget(10, campaigns=100)['per_store_queued_requests']['fullstats'])


if __name__ == '__main__':
    unittest.main()
