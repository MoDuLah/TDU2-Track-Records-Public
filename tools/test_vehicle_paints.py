import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location("paint_export", Path(__file__).with_name("export_vehicle_paints.py"))
paint_export = importlib.util.module_from_spec(spec)
spec.loader.exec_module(paint_export)


class MaterialTests(unittest.TestCase):
    def test_ramp_base_colour_overrides_white_diffuse_and_skips_runtime_slots(self):
        data = bytearray(0x350 + 2 * 32)
        struct.pack_into("<4f", data, 208, 1, 1, 1, 1)
        struct.pack_into("<2H", data, 0x31A, 1, 1)
        struct.pack_into("<8sHHI", data, 0x350, b"RUNTIME", 2, 1, 16)
        struct.pack_into("<8sHHI", data, 0x360, b"RAMPCLR1", 2, 0, 16)
        struct.pack_into("<4f", data, 0x380, 1, 218 / 255, 0, 1)
        self.assertEqual(paint_export.material_colour(data), "#FFDA00")

    def test_older_material_reads_diffuse_and_rejects_invalid_components(self):
        data = bytearray(224)
        struct.pack_into("<4f", data, 208, 0, 0.2, 1, 1)
        self.assertEqual(paint_export.material_colour(data), "#0033FF")
        struct.pack_into("<f", data, 208, float("nan"))
        self.assertIsNone(paint_export.material_colour(data))

    def test_malformed_material_library_is_rejected(self):
        with self.assertRaises(ValueError):
            paint_export.material_colours(b"bad material")
        data = bytearray(32)
        struct.pack_into("<I", data, 8, len(data))
        data[12:16] = b".2DM"
        struct.pack_into("<4s3I", data, 16, b"MATA", 0, 4096, 16)
        with self.assertRaises(ValueError):
            paint_export.material_colours(data)

    def test_two_tone_options_keep_factory_order_and_unresolved_finishes(self):
        class Assets:
            def materials(self, path):
                return {"red": "#FF0000", "white": "#FFFFFF"}

        def row(name, primary, secondary):
            return {"_car": 1, "_color_name": name, "_color_id_1": primary,
                    "_color_id_2": secondary, "_price": 0}

        data = {"DBCarPhysicsData": [{"_id_car": 1, "_car_file_name": "sample"}],
                "DBCarsAndColors": [row("White on red", "red", "white"),
                                   row("Red", "red", "##"),
                                   row("Special finish", "", "")]}
        rows = paint_export.build_catalogue(Assets(), data)
        self.assertEqual([r["paint_index"] for r in rows], [0, 1, 2])
        self.assertEqual([r["primary"] for r in rows], ["#FF0000", "#FF0000", None])
        self.assertEqual(rows[0]["secondary"], "#FFFFFF")
        self.assertTrue(rows[0]["has_secondary"])
        self.assertFalse(rows[1]["has_secondary"])


if __name__ == "__main__":
    unittest.main()

