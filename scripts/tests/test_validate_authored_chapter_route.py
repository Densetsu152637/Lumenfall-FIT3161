from pathlib import Path
import importlib.util, shutil, tempfile, unittest

SCRIPT=Path(__file__).resolve().parents[1]/'validate_authored_chapter_route.py'
spec=importlib.util.spec_from_file_location('chapter_route',SCRIPT)
route=importlib.util.module_from_spec(spec);spec.loader.exec_module(route)

class AuthoredChapterRouteValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.root=Path(self.temp.name)
        self.areas=self.root/'Lumenfall/Assets/_Project/World/Areas'
        for folder in ('','Prefabs','Portals'):
            source=route.AREAS/folder;destination=self.areas/folder;destination.mkdir(parents=True,exist_ok=True)
            for pattern in ('*.asset','*.prefab','*.meta'):
                for path in source.glob(pattern):shutil.copy2(path,destination/path.name)
    def tearDown(self):self.temp.cleanup()
    def change(self,path,before,after):
        text=path.read_text(encoding='utf-8-sig');self.assertIn(before,text)
        path.write_text(text.replace(before,after,1),encoding='utf-8')
    def test_actual_live_route_accepts_all_required_round_trips(self):
        errors,maps,edges=route.validate(self.root)
        self.assertEqual(errors,[]);self.assertEqual((maps,edges),(6,10))
    def test_out_of_bounds_arrival_fails(self):
        self.change(self.areas/'Portals/campus_P.C05.B01.asset','arrivalOffset: {x: 0, y: -1}','arrivalOffset: {x: 99, y: -1}')
        errors,_,_=route.validate(self.root)
        self.assertTrue(any('P.C05.B01: blocked/out-of-bounds arrival' in error for error in errors),errors)
    def test_required_route_cannot_arrive_in_reserved_endpoint(self):
        self.change(self.areas/'Portals/B01.Ground_P.B01.C05.asset','reserved: 0','reserved: 1')
        errors,_,_=route.validate(self.root)
        self.assertTrue(any('destination reserved' in error for error in errors),errors)
        self.assertTrue(any('Required route missing: campus -> B01.Ground' in error for error in errors),errors)
    def test_baked_portal_location_mismatch_fails(self):
        self.change(self.areas/'Prefabs/campus.prefab','anchorCell: {x: 40, y: 80}','anchorCell: {x: 41, y: 80}')
        errors,_,_=route.validate(self.root)
        self.assertTrue(any('P.C05.B01: prefab/content cell mismatch' in error for error in errors),errors)
    def test_closed_dormitory_regions_need_enforcement(self):
        self.change(self.areas/'Prefabs/B04.Ground.prefab','Lumenfall.Runtime::Lumenfall.DataDriven.ReservedAreaRegions','Lumenfall.Runtime::Lumenfall.DataDriven.MissingComponent')
        errors,_,_=route.validate(self.root)
        self.assertIn('B04.Ground: no live reserved-region enforcement',errors)
    def test_production_scene_authorities_are_wired_once(self):
        self.assertEqual(route.validate_scene(),[])
    def test_blocked_cells_cannot_be_crossed_by_route_search(self):
        self.assertFalse(route.reachable((0,0),(2,0),(3,1),{(1,0)}))
        self.assertTrue(route.reachable((0,0),(2,0),(3,2),{(1,0)}))

if __name__=='__main__':unittest.main()
