from locust import HttpUser, task, between
import random

class WebshopUser(HttpUser):
    # Felhasználók 1-3 másodpercet várnak kérések között
    wait_time = between(1, 3)
    
    @task(10)  # 10x gyakoribb (böngészés)
    def browse_products(self):
        self.client.get("/api/products?limit=20")
    
    @task(5)  # 5x gyakoribb (termék megtekintés)
    def view_product(self):
        product_id = random.randint(1, 100)
        self.client.get(f"/api/products/{product_id}")
    
    @task(3)  # 3x gyakoribb (keresés)
    def search(self):
        self.client.get("/api/search?q=elektronika")
    
    @task(1)  # 1x gyakoribb (kosárba tétel)
    def add_to_cart(self):
        self.client.post("/api/cart/add", json={
            "productId": random.randint(1, 100),
            "quantity": 1
        })